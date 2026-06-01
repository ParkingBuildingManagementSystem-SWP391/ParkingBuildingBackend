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

        // ==========================================
        // LUỒNG 1: XỬ LÝ ĐẶT CHỖ (BOOKING TRÊN WEB) - ĐÃ CẬP NHẬT JWT
        // ==========================================
        public async Task<bool> BookSlotAsync(int userId, BookSlotRequest request) // Nhận thêm userId từ Token
        {
            // 1. NGHIỆP VỤ ANTI-SPAM: Sử dụng userId an toàn từ Token
            var hasActiveBooking = await _repository.HasActiveReservationAsync(userId);
            if (hasActiveBooking)
                throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

            // 2. Lấy thông tin Slot
            var slot = await _repository.GetSlotByIdAsync(request.SlotId);

            // 3. NGHIỆP VỤ SLOT: Kiểm tra tính hợp lệ và trạng thái
            if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot?.IsDeleted == true)
                throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

            // 4. NGHIỆP VỤ LOẠI XE: Kiểm tra loại xe gửi lên có khớp với thiết kế của Slot không
            if (slot.TypeId != request.TypeId)
                throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này (Ví dụ: Không thể đỗ ô tô vào chỗ xe máy).");

            // TỰ ĐỘNG SINH VÉ QR Ở TRẠNG THÁI ACTIVE
            var ticket = new Ticket
            {
                TicketCode = $"QR_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            // 5. Cập nhật trạng thái sang "Reserved" vì khách mới đặt trên web, xe chưa tới bãi
            slot.SlotStatus = ParkingStatuses.SlotReserved;

            // 6. Tạo Session lưu lịch sử ở trạng thái Chờ khách đến ("Reserved")
            var newSession = new ParkingSession
            {
                UserId = userId, // Gán userId từ Token thay vì request.UserId
                SlotId = request.SlotId,
                LicenseVehicle = request.LicenseVehicle,
                TypeId = request.TypeId,
                BookingTime = DateTime.UtcNow, // Bắt đầu tính 15 phút từ đây
                SessionStatus = ParkingStatuses.SessionReserved,
                Ticket = ticket, // EF Core tự động liên kết lưu trữ
                IsDeleted = false
            };
            Console.WriteLine($"---> DEBUG Token: UserId = {userId}, SlotId = {request.SlotId}, TypeId = {request.TypeId}");

            // 7. Gọi Repository lưu vào DB thông qua Transaction
            await _repository.CreateSessionAsync(newSession, slot);

            return true;
        }

        // ==========================================
        // LUỒNG 2: XỬ LÝ KHI XE ĐẾN CỔNG BÃI (CHECK-IN) - GIỮ NGUYÊN
        // ==========================================
        public async Task<bool> CheckInVehicleAsync(CheckInRequest request)
        {
            // Bảo vệ an toàn dữ liệu đầu vào
            if (request == null || string.IsNullOrEmpty(request.LicenseVehicle)) return false;

            // 1. Gọi hàm từ Repository để lấy Session kèm Slot lên dựa vào biển số xe
            var session = await _repository.GetReservedSessionByLicenseAsync(request.LicenseVehicle);

            // Nếu không tìm thấy đơn, nghĩa là khách chưa đặt, hoặc đã bị file chạy ngầm hủy sau 15p rồi
            if (session == null) return false;

            // 2. CẬP NHẬT TRẠNG THÁI: Lúc này xe mới chính thức vào bãi -> Đổi sang InProgress
            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow; // Ghi nhận thời gian vào bãi thực tế
            session.CheckInImageUrl = request.CheckInImageUrl;

            // 3. Chuyển trạng thái Slot sang thực tế đang bị chiếm giữ (Occupied)
            if (session.Slot != null)
            {
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            // 4. Gọi hàm cập nhật đồng bộ của Repository để thực thi lưu dữ liệu an toàn
            await _repository.UpdateSessionAndSlotAsync(session, session.Slot);
            return true;
        }

        // ==========================================
        // LUỒNG BỔ SUNG: XỬ LÝ KHÁCH VÃNG LAI (WALK-IN) - GIỮ NGUYÊN
        // ==========================================
        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.LicenseVehicle))
                throw new Exception("Thông tin biển số xe không hợp lệ.");

            // 1. Tìm ô đỗ trống bất kỳ cho loại xe và Khóa dòng
            var slot = await _repository.GetAvailableSlotForWalkInAsync(request.TypeId);
            if (slot == null)
                throw new Exception("Hiện tại bãi xe đã đầy vị trí trống cho loại xe của bạn!");

            // 2. Tự sinh một mã vé mới tại cổng
            var ticket = new Ticket
            {
                TicketCode = request.CardOrTicketId ?? $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            // 3. Chuyển trạng thái ô đỗ sang Occupied
            slot.SlotStatus = ParkingStatuses.SlotOccupied;

            // 4. Tạo trực tiếp ParkingSession ở trạng thái xe đang trong bãi ("InProgress")
            var newSession = new ParkingSession
            {
                UserId = null, // Khách vãng lai không có tài khoản (null là đúng)
                SlotId = slot.SlotId,
                LicenseVehicle = request.LicenseVehicle,
                TypeId = request.TypeId,
                CheckInTime = DateTime.UtcNow,
                CheckInImageUrl = request.CheckInImageUrl,
                SessionStatus = ParkingStatuses.SessionInProgress,
                Ticket = ticket,
                IsDeleted = false
            };

            // 5. Lưu thông qua Transaction
            await _repository.CreateSessionAsync(newSession, slot);

            return new WalkInResponse
            {
                SessionId = newSession.SessionId,
                TicketCode = ticket.TicketCode,
                SlotName = slot.SlotName,
                Status = ParkingStatuses.SessionInProgress
            };
        }
    }
}