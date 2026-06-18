using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class CheckInService : ICheckInService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ILogger<CheckInService> _logger;

        public CheckInService(
            IParkingRepository parkingRepository,
            ILogger<CheckInService> logger)
        {
            _parkingRepository = parkingRepository;
            _logger = logger;
        }

        public async Task<bool> CheckInVehicleAsync(CheckInRequest request)
        {
            if (request == null)
            {
                _logger.LogWarning("Check-in thất bại: Dữ liệu Request rỗng (null).");
                return false;
            }
            _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé/Mã QR: {TicketCode}",
                request.LicenseVehicle, request.TicketCode);

            string? cleanTicketCode = string.IsNullOrWhiteSpace(request.TicketCode) ? null : request.TicketCode.Trim();
            string? cleanLicense = null;
            if (!string.IsNullOrWhiteSpace(request.LicenseVehicle) && request.LicenseVehicle.Trim().ToLower() != "string")
            {
                if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string validatedPlate))
                {
                    throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
                }
                cleanLicense = validatedPlate;
            }

            if (cleanTicketCode == null && cleanLicense == null)
            {
                _logger.LogWarning("Check-in thất bại: Cả Biển số xe và Mã vé đều rỗng.");
                return false;
            }

            if (cleanLicense != null)
            {
                var alreadyInParking = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
                if (alreadyInParking != null)
                {
                    _logger.LogWarning("Check-in thất bại: Xe biển số '{LicensePlate}' đã có một phiên đỗ xe đang hoạt động trong bãi (SessionId: {SessionId}).",
                        cleanLicense, alreadyInParking.SessionId);
                    return false;
                }
            }

            ParkingSession? session = null;

            if (cleanTicketCode != null)
            {
                if (int.TryParse(cleanTicketCode, out int ticketId))
                {
                    session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                }

                if (session != null && cleanLicense != null)
                {
                    if (!string.IsNullOrEmpty(session.LicenseVehicle))
                    {
                        if (cleanLicense.ToUpper() != session.LicenseVehicle.Trim().ToUpper())
                        {
                            _logger.LogWarning("Check-in thất bại: Biển số thực tế '{Actual}' không khớp với biển số đã đăng ký đặt chỗ '{Reserved}' trên vé/Session {SessionId}.",
                                cleanLicense, session.LicenseVehicle, session.SessionId);
                            return false;
                        }
                    }
                }
            }
            else if (cleanLicense != null)
            {
                session = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
            }

            if (session == null)
            {
                _logger.LogWarning("Check-in thất bại: Không tìm thấy phiên đặt chỗ (Reservation) tương ứng với Biển số '{License}' hoặc Mã vé '{Ticket}'.",
                    cleanLicense, cleanTicketCode);
                return false;
            }

            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow;
            session.CheckInImageUrl = string.IsNullOrWhiteSpace(request.CheckInImageUrl) ? null : request.CheckInImageUrl;
            if (session.Slot != null)
            {
                if (session.Slot.SlotStatus == ParkingStatuses.SlotOccupied)
                {
                    _logger.LogError("Check-in thất bại: Vị trí đỗ {SlotName} đã bị xe khác chiếm dụng cho Session {SessionId}.",
                        session.Slot.SlotName, session.SessionId);
                    throw new Exception($"Chỗ đỗ {session.Slot.SlotName} hiện đã bị xe khác chiếm dụng. Vui lòng kiểm tra lại vị trí đỗ.");
                }
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            if (cleanTicketCode != null && session.Ticket != null)
            {
                session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
            }

            await _parkingRepository.UpdateSessionAndSlotAsync(session, session.Slot!);
            _logger.LogInformation("Check-in THÀNH CÔNG: Xe '{LicensePlate}' đã vào bãi. Ô đỗ phân phối: {SlotName}. SessionId: {SessionId}.",
                cleanLicense ?? session.LicenseVehicle, session.Slot?.SlotName ?? "N/A", session.SessionId);
            return true;
        }

        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request)
        {
            if (request == null)
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Yêu cầu dữ liệu không hợp lệ!" };
            }

            if (string.IsNullOrWhiteSpace(request.LicenseVehicle) || request.LicenseVehicle.Trim().ToLower() == "string")
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Vui lòng cung cấp biển số xe!" };
            }
            
            if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string cleanedLicense))
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = LicensePlateHelper.GetErrorMessage()
                };              
            }
            string cleanLicense = cleanedLicense;

            var activeSession = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
            if (activeSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} hiện đang có một lượt đỗ chưa hoàn thành trong bãi!"
                };
            }

            var reservedSession = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
            if (reservedSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} đang có lịch ĐẶT TRƯỚC chưa check-in! Vui lòng quét mã vé đặt trước hoặc thực hiện Check-in theo lịch đặt."
                };
            }

            var ticket = new Ticket
            {
                TicketCode = $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            string? checkInImageUrl = (string.IsNullOrWhiteSpace(request.CheckInImageUrl) || request.CheckInImageUrl.Trim().ToLower() == "string") ? null : request.CheckInImageUrl;
            
            var newSession = await _parkingRepository.CreateWalkInSessionWithLockAsync(cleanLicense, request.VehicleTypeId, checkInImageUrl, ticket);
            if (newSession == null)
            {
                return new WalkInResponse { Status = "Full", TicketCode = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!" };
            }
            
            var slot = newSession.Slot ?? await _parkingRepository.GetSlotByIdAsync(newSession.SlotId);
            if (slot == null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"Hệ thống không tìm thấy slot id tương ứng"
                };
            }

            return new WalkInResponse
            {
                SessionId = newSession.SessionId,
                SlotId = slot.SlotId,
                TicketCode = ticket.TicketCode,
                SlotName = slot.SlotName,
                LicenseVehicle = newSession.LicenseVehicle,
                CheckInTime = newSession.CheckInTime ?? DateTime.UtcNow,
                Status = ParkingStatuses.SessionInProgress
            };
        }
    }
}
