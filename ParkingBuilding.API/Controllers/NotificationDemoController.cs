using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ParkingBuilding.API.Hubs;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.IService;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationDemoController : ControllerBase
    {
        private readonly INotificationService _notificationService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IHubContext<DriverNotificationHub> _hubContext;

        public NotificationDemoController(
            INotificationService notificationService,
            IUnitOfWork unitOfWork,
            IHubContext<DriverNotificationHub> hubContext)
        {
            _notificationService = notificationService;
            _unitOfWork = unitOfWork;
            _hubContext = hubContext;
        }

        /// <summary>
        /// DEMO 1: Biến động số dư ví (Gửi đích danh cho cá nhân) sử dụng INotificationService.
        /// Thể hiện mô hình Clean Architecture: BLL gọi dịch vụ thông báo qua Interface INotificationService.
        /// </summary>
        [HttpPost("wallet-transaction")]
        [Authorize(Roles = "Registered_Driver")]
        public async Task<IActionResult> SimulateWalletTransaction([FromBody] WalletTransactionDemoRequest request)
        {
            if (request.Amount == 0)
                return BadRequest("Số tiền giao dịch không hợp lệ.");

            // 1. Thực hiện logic nghiệp vụ trong database (ví dụ: cập nhật số dư ví)
            // (Trong thực tế việc này sẽ nằm ở WalletService thuộc tầng BLL)
            var wallet = await _unitOfWork.Notifications.GetFirstOrDefaultAsync(n => false); // Dummy call minh họa dùng UoW

            // 2. Định nghĩa nội dung thông báo
            string title = request.Amount > 0 ? "Biến động số dư: +Cộng tiền" : "Biến động số dư: -Trừ tiền";
            string type = NotificationTypes.WalletTransaction;
            string content = $"Tài khoản ví của bạn vừa {(request.Amount > 0 ? "được cộng" : "bị trừ")} {Math.Abs(request.Amount):N0} VNĐ. Lý do: {request.Reason}.";

            // 3. Sử dụng NotificationService để Lưu DB và Bắn SignalR (Database-First)
            var notification = await _notificationService.SendToUserAsync(request.UserId, title, content, type);

            return Ok(new
            {
                isSuccess = true,
                message = "Giao dịch ví đã được xử lý và thông báo real-time thành công.",
                data = notification
            });
        }

        /// <summary>
        /// DEMO 2: Cập nhật giá ca đỗ xe (Gửi cho nhóm - Group) sử dụng INotificationService.
        /// Gửi thông báo đến toàn bộ các tài xế đang tham gia nhóm của bãi xe / ca đỗ đó.
        /// </summary>
        [HttpPost("shift-price-update")]
        public async Task<IActionResult> SimulateShiftPriceUpdate([FromBody] ShiftPriceDemoRequest request)
        {
            if (request.NewPrice <= 0)
                return BadRequest("Giá mới phải lớn hơn 0 VNĐ.");

            // 1. Thực hiện logic lưu giá mới vào cơ sở dữ liệu qua UoW
            // (Trong thực tế nằm ở ParkingShiftService thuộc tầng BLL)

            // 2. Định nghĩa nội dung và group nhận thông tin
            string groupName = $"shift-parking-{request.BuildingId}"; // Ví dụ: shift-parking-BuildingA
            string title = "Cập nhật giá đỗ xe theo ca";
            string type = NotificationTypes.ShiftParkingPriceUpdate;
            string content = $"Ca đỗ xe tại {request.BuildingName} đã thay đổi giá mới là {request.NewPrice:N0} VNĐ/giờ, áp dụng từ thời điểm này.";

            // 3. Gọi dịch vụ để lưu DB cho các driver và phát SignalR tới Group
            await _notificationService.SendToGroupAsync(groupName, title, content, type);

            return Ok(new
            {
                isSuccess = true,
                message = $"Đã cập nhật giá mới và phát thông báo tới nhóm {groupName} thành công."
            });
        }

        /// <summary>
        /// DEMO 3: Minh họa việc INJECT TRỰC TIẾP IHubContext vào Controller (Presentation layer) 
        /// để xử lý Lưu DB xong mới bắn Real-time (Theo yêu cầu kỹ thuật).
        /// </summary>
        [HttpPost("direct-hub-context-demo")]
        public async Task<IActionResult> DirectHubContextDemo([FromBody] DirectDemoRequest request)
        {
            // 1. Khởi tạo đối tượng Notification
            var notification = new Notification
            {
                UserId = request.UserId,
                Title = request.Title,
                Content = request.Content,
                NotificationType = request.Type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            // 2. Thực hiện LƯU DATABASE trước (Database-First)
            await _unitOfWork.Notifications.AddAsync(notification);
            
            // Chờ lưu DB thành công
            bool dbSaved = await _unitOfWork.SaveChangesAsync();

            if (!dbSaved)
            {
                return StatusCode(500, "Lưu thông báo vào database thất bại. Huỷ phát real-time.");
            }

            // 3. Sau khi DB lưu thành công -> Bắn SignalR
            await _hubContext.Clients.User(request.UserId.ToString()).SendAsync("ReceiveNotification", new
            {
                notificationId = notification.NotificationId,
                userId = notification.UserId,
                title = notification.Title,
                content = notification.Content,
                notificationType = notification.NotificationType,
                isRead = notification.IsRead,
                createdAt = notification.CreatedAt
            });

            return Ok(new
            {
                isSuccess = true,
                message = "Đã lưu DB thành công và bắn SignalR trực tiếp qua IHubContext.",
                data = notification
            });
        }
    }

    public class WalletTransactionDemoRequest
    {
        public int UserId { get; set; }
        public decimal Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class ShiftPriceDemoRequest
    {
        public string BuildingId { get; set; } = string.Empty;
        public string BuildingName { get; set; } = string.Empty;
        public decimal NewPrice { get; set; }
    }

    public class DirectDemoRequest
    {
        public int UserId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
