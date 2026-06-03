using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class ParkingService : IParkingService
    {
        private readonly IParkingRepository _repository;

        public ParkingService(IParkingRepository repository)
        {
            _repository = repository;
        }

        // ===========================================================
        //          LUỒNG 1: XỬ LÝ ĐẶT CHỖ (BOOKING TRÊN WEB) 
        // ============================================================
        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request) 
        {
            var hasActiveBooking = await _repository.HasActiveReservationAsync(userId);
            if (hasActiveBooking)
                throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

            var slot = await _repository.GetSlotByIdAsync(request.SlotId);

            if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot?.IsDeleted == true)
                throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

            if (slot.TypeId != request.TypeId)
                throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này (Ví dụ: Không thể đỗ ô tô vào chỗ xe máy).");

            var ticket = new Ticket
            {
                TicketCode = $"QR_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            slot.SlotStatus = ParkingStatuses.SlotReserved;

            var newSession = new ParkingSession
            {
                UserId = userId, 
                SlotId = request.SlotId,
                LicenseVehicle = request.LicenseVehicle,
                TypeId = request.TypeId,
                BookingTime = DateTime.UtcNow, 
                SessionStatus = ParkingStatuses.SessionReserved,
                Ticket = ticket, 
                IsDeleted = false
            };
            Console.WriteLine($"---> DEBUG Token: UserId = {userId}, SlotId = {request.SlotId}, TypeId = {request.TypeId}");

            await _repository.CreateSessionAsync(newSession, slot);

            // ============================================
            //  XỬ LÝ SINH MÃ QR TỪ TICKET CODE VỪA TẠO
            // ============================================
            string base64QR = "";
            using (var qrGenerator = new QRCoder.QRCodeGenerator())
            using (var qrCodeData = qrGenerator.CreateQrCode(ticket.TicketCode, QRCoder.QRCodeGenerator.ECCLevel.Q))
            using (var qrCode = new QRCoder.PngByteQRCode(qrCodeData)) 
            {
                byte[] qrCodeBytes = qrCode.GetGraphic(20);
                base64QR = $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
            }

            return new BookSlotResponse
            {
                IsSuccess = true,
                Message = "Đặt chỗ đỗ xe thành công! Vui lòng tới bãi và quét mã check-in trong vòng 15 phút.",
                TicketCode = ticket.TicketCode,
                SlotId = newSession.SlotId.ToString(),
                BookingTime = newSession.BookingTime ?? DateTime.UtcNow, 
                QrCodeBase64 = base64QR
            };
        }

        // =========================================================================
        //              LUỒNG 2: XỬ LÝ KHI XE ĐẾN CỔNG BÃI (CHECK-IN) 
        // =========================================================================
        public async Task<bool> CheckInVehicleAsync(ParkingBuilding.Service.DTOs.CheckInRequest request)
        {
            
            if (request == null) return false;
            if (string.IsNullOrEmpty(request.TicketCode) && string.IsNullOrEmpty(request.LicenseVehicle)) return false;

            ParkingSession? session = null;

            // ==============================================================================
            // KỊCH BẢN A: Khách check-in bằng cách quét mã QR (Trường TicketCode có dữ liệu)
            // ==============================================================================
            if (!string.IsNullOrEmpty(request.TicketCode))
            {
                session = await _repository.GetReservedSessionByTicketCodeAsync(request.TicketCode);
            }

            // =============================================================================
            // KỊCH BẢN B: Khách check-in bằng Biển số xe (Trường LicenseVehicle có dữ liệu)
            // =============================================================================
            else if (!string.IsNullOrEmpty(request.LicenseVehicle))
            {
                session = await _repository.GetReservedSessionByLicenseAsync(request.LicenseVehicle);
            }

            if (session == null) return false;

            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow; 
            session.CheckInImageUrl = request.CheckInImageUrl;

            if (session.Slot != null)
            {
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            if (!string.IsNullOrEmpty(request.TicketCode) && session.Ticket != null)
            {
                session.Ticket.TicketStatus = "Used";
            }

            await _repository.UpdateSessionAndSlotAsync(session, session.Slot!);

            return true;
        }

        // =========================================================================
        //              LUỒNG 3: XỬ LÝ KHÁCH VÃNG LAI (WALK-IN) 
        // =========================================================================
        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.LicenseVehicle))
                throw new Exception("Thông tin yêu cầu không hợp lệ. Vui lòng nhập biển số xe!");

            var slot = await _repository.GetAvailableSlotForWalkInAsync(request.VehicleTypeId);
            if (slot == null)
                throw new Exception("Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!");

            var ticket = new Ticket
            {
                TicketCode = $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = "Used" 
            };

            slot.SlotStatus = ParkingStatuses.SlotOccupied;

            var newSession = new ParkingSession
            {
                UserId = null, 
                SlotId = slot.SlotId,
                LicenseVehicle = request.LicenseVehicle.Trim().ToUpper(), 
                TypeId = request.VehicleTypeId, 
                CheckInTime = DateTime.UtcNow,
                CheckInImageUrl = request.CheckInImageUrl, 
                SessionStatus = ParkingStatuses.SessionInProgress, 
                Ticket = ticket,
                IsDeleted = false
            };

            await _repository.CreateSessionAsync(newSession, slot);

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

        public async Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId)
        {
            var slots = await _repository.GetSlotsByFloorIdAsync(floorId);

            // Thực hiện ánh xạ dữ liệu (Mapping) từ Entity sang DTO
            return slots.Select(s => new ParkingSlotResponseDto
            {
                SlotName = s.SlotName,
                SlotStatus = s.SlotStatus,
                TypeId = s.TypeId
            }).ToList();
        }

    }
}