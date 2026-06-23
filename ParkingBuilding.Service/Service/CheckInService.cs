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
        private readonly IImageStorageService _imageStorageService;
        private readonly IAiRecognitionService _aiRecognitionService;

        public CheckInService(
            IParkingRepository parkingRepository,
            ILogger<CheckInService> logger,
            IImageStorageService imageStorageService,
            IAiRecognitionService aiRecognitionService)
        {
            _parkingRepository = parkingRepository;
            _logger = logger;
            _imageStorageService = imageStorageService;
            _aiRecognitionService = aiRecognitionService;
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

            string ? checkInImageUrl = null;
                        if (!string.IsNullOrEmpty(request.ImageUrl))
                            {
                                // Lấy trực tiếp URL ảnh đã qua xác nhận ở Frontend
                checkInImageUrl = request.ImageUrl;
                            }
                        else if (request.ImageFile != null && request.ImageFile.Length > 0)
                            {
                                // Trường hợp chạy tự động, không có xác nhận thủ công từ FE
                checkInImageUrl = await _imageStorageService.UploadImageAsync(request.ImageFile, "parking_checkin");
                                try
                {
                    string detectedPlate = await _aiRecognitionService.PredictLicensePlateAsync(checkInImageUrl);
                    request.LicenseVehicle = detectedPlate;
                                    }
                                catch (Exception ex)
                {
                    _logger.LogWarning("AI Nhận diện cổng vào lỗi: {Msg}. Tiếp tục dùng biển số nhập tay nếu có.", ex.Message);
                                    }
                            }
                _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé: {TicketCode}",
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
            session.CheckInImageUrl = checkInImageUrl;
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

            string? checkInImageUrl = null;
                        if (!string.IsNullOrEmpty(request.ImageUrl))
                            {
                checkInImageUrl = request.ImageUrl;
                            }
                        else if (request.ImageFile != null && request.ImageFile.Length > 0)
                            {
                checkInImageUrl = await _imageStorageService.UploadImageAsync(request.ImageFile, "parking_checkin");
                                if (request.VehicleTypeId == 1) // Xe đạp
                                    {
                    request.LicenseVehicle = $"BIKE_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                                    }
                                else if (string.IsNullOrEmpty(request.LicenseVehicle) || request.LicenseVehicle == "string")
                                    {
                                        try
                    {
                        string detectedPlate = await _aiRecognitionService.PredictLicensePlateAsync(checkInImageUrl);
                        request.LicenseVehicle = detectedPlate;
                                            }
                                        catch (Exception ex)
                    {
                        _logger.LogWarning("AI Nhận diện Walk-in lỗi: {Msg}", ex.Message);
                                            }
                                    }
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


            //

            //
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

        public async Task<ScanCheckInResponse> ScanQrCheckInAsync(string ticketCode, string? detectedPlate)
        {
            var isTicketCodeEmpty = string.IsNullOrWhiteSpace(ticketCode) || 
                                    ticketCode.Trim().ToLower() == "null" || 
                                    ticketCode.Trim().ToLower() == "undefined";

            if (isTicketCodeEmpty && string.IsNullOrWhiteSpace(detectedPlate))
            {
                return new ScanCheckInResponse { IsSuccess = false, Message = "Mã QR vé hoặc biển số xe không hợp lệ." };
            }

            ParkingSession? session = null;
            string cleanTicketCode = isTicketCodeEmpty ? "" : ticketCode.Trim();

            if (isTicketCodeEmpty && !string.IsNullOrWhiteSpace(detectedPlate))
            {
                // Truy vấn đặt chỗ theo biển số xe thực tế khi không có mã QR
                session = await _parkingRepository.GetReservedSessionByLicenseAsync(detectedPlate.Trim());
            }
            else
            {
                if (int.TryParse(cleanTicketCode, out int ticketId))
                {
                    session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                    if (session == null)
                    {
                        session = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanTicketCode);
                    }
                }
            }

            if (session == null)
            {
                return new ScanCheckInResponse { IsSuccess = false, Message = "Không tìm thấy thông tin đặt chỗ." };
            }

            // ĐỐI CHIẾU BIỂN SỐ XE THỰC TẾ VS BIỂN ĐĂNG KÝ TRÊN VÉ ĐẶT CHỖ
            if (!string.IsNullOrEmpty(detectedPlate) && detectedPlate.Trim().ToLower() != "string")
            {
                var cleanDetected = detectedPlate.Trim().Replace("-", "").Replace(".", "").ToUpper();
                var cleanRegistered = session.LicenseVehicle.Trim().Replace("-", "").Replace(".", "").ToUpper();

                if (cleanRegistered != cleanDetected)
                {
                    return new ScanCheckInResponse
                    {
                        IsSuccess = false,
                        Message = $"Cảnh báo bảo mật: Vé QR đăng ký cho xe {session.LicenseVehicle}, không khớp với xe thực tế tại cổng là {detectedPlate}!"
                    };
                }
            }

            return new ScanCheckInResponse
            {
                IsSuccess = true,
                Message = "Quét mã QR và đối khớp biển số thành công.",
                SessionId = session.SessionId,
                LicenseVehicle = session.LicenseVehicle,
                SlotName = session.Slot?.SlotName ?? "N/A",
                VehicleTypeName = session.Type?.TypeName ?? "N/A",
                ExpectedCheckInTime = session.ExpectedCheckInTime,
                BookingTime = session.BookingTime,
                DriverName = session.User?.Username ?? "N/A",
                DriverPhone = session.User?.PhoneNumber ?? "N/A",
                DriverEmail = session.User?.Email ?? "N/A",
                TicketCode = session.Ticket?.TicketCode ?? cleanTicketCode,
                RequiresPayment = session.Invoice != null && session.Invoice.PaymentStatus == "PENDING",
                PaymentStatus = session.Invoice?.PaymentStatus ?? "NoPayment"
            };
        }
    }
}
