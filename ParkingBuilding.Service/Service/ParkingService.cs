using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class ParkingService : IParkingService
    {
        private readonly IParkingRepository _repository;

        public ParkingService(IParkingRepository repository) { _repository = repository; }

        public async Task<bool> BookSlotAsync(BookSlotRequest request)
        {
            // 1. NGHIỆP VỤ ANTI-SPAM: Kiểm tra User có đang giữ chỗ nào khác chưa?
            var hasActiveBooking = await _repository.HasActiveReservationAsync(request.UserId);
            if (hasActiveBooking)
                throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

            // 2. Lấy thông tin Slot
            var slot = await _repository.GetSlotByIdAsync(request.SlotId);

            // 3. NGHIỆP VỤ SLOT: Kiểm tra tính hợp lệ và trạng thái
            if (slot == null || slot.SlotStatus != "Available" || slot.IsDeleted)
                throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

            // 4. NGHIỆP VỤ LOẠI XE: Kiểm tra loại xe gửi lên có khớp với thiết kế của Slot không
            if (slot.TypeId != request.TypeId)
                throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này (Ví dụ: Không thể đỗ ô tô vào chỗ xe máy).");

            // 5. Nếu qua hết các bài test -> Cập nhật trạng thái Slot
            slot.SlotStatus = "InProgress";

            // 6. Tạo Session lưu lịch sử
            var newSession = new ParkingSession
            {
                UserId = request.UserId,
                SlotId = request.SlotId,
                LicenseVehicle = request.LicenseVehicle,
                TypeId = request.TypeId,
                BookingTime = DateTime.UtcNow,
                SessionStatus = "InProgress",
                IsDeleted = false
            };

            // 7. Gọi Repository lưu vào DB thông qua Transaction
            await _repository.CreateSessionAsync(newSession, slot);

            return true;
        }
    }
}
