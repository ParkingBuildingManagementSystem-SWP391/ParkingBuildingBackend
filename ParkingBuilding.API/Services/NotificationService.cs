using Microsoft.AspNetCore.SignalR;
using ParkingBuilding.API.Hubs;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.IService;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Services
{
    /// <summary>
    /// Triển khai thực tế dịch vụ thông báo (Notification Service).
    /// Đóng gói nghiệp vụ: Lưu DB trước -> Đẩy tin nhắn qua SignalR Hub sau.
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IHubContext<DriverNotificationHub> _hubContext;

        public NotificationService(
            IUnitOfWork unitOfWork,
            IHubContext<DriverNotificationHub> hubContext)
        {
            _unitOfWork = unitOfWork;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Tạo và gửi thông báo đích danh tới một tài xế cụ thể.
        /// </summary>
        public async Task<Notification> SendToUserAsync(int userId, string title, string content, string type)
        {
            // 1. Tạo thực thể Notification
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Content = content,
                NotificationType = type,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            // 2. Lưu vào Cơ sở dữ liệu (Database-First)
            await _unitOfWork.Notifications.AddAsync(notification);
            await _unitOfWork.SaveChangesAsync();

            // 3. Trả kết quả thành công và Bắn SignalR đích danh tới User
            // Note: SignalR dùng userId (string) làm UserIdentifier trong JWT ClaimTypes.NameIdentifier
            await _hubContext.Clients.User(userId.ToString()).SendAsync("ReceiveNotification", new
            {
                notificationId = notification.NotificationId,
                userId = notification.UserId,
                title = notification.Title,
                content = notification.Content,
                notificationType = notification.NotificationType,
                isRead = notification.IsRead,
                createdAt = notification.CreatedAt
            });

            return notification;
        }

        /// <summary>
        /// Tạo và gửi thông báo diện rộng tới một nhóm đối tượng tài xế đang lắng nghe.
        /// Đồng thời lưu vết thông báo trong DB cho tất cả tài xế thuộc nhóm/hệ thống để họ có thể xem lại lịch sử.
        /// </summary>
        public async Task SendToGroupAsync(string groupName, string title, string content, string type)
        {
            // 1. Lấy danh sách tất cả tài xế đang hoạt động từ DB
            var allUsers = await _unitOfWork.Users.GetAllUsersWithRolesAsync();
            var activeDrivers = allUsers
                .Where(u => u.Role?.RoleName == "Registered_Driver")
                .ToList();

            // 2. Tạo và lưu thông báo cho mỗi tài xế vào DB (Database-First)
            foreach (var driver in activeDrivers)
            {
                var notification = new Notification
                {
                    UserId = driver.UserId,
                    Title = title,
                    Content = content,
                    NotificationType = type,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.Notifications.AddAsync(notification);
            }

            // Lưu thay đổi vào DB trước khi phát tin
            await _unitOfWork.SaveChangesAsync();

            // 3. Phát tin real-time tới Group tương ứng qua SignalR
            await _hubContext.Clients.Group(groupName).SendAsync("ReceiveNotification", new
            {
                title = title,
                content = content,
                notificationType = type,
                groupName = groupName,
                createdAt = DateTime.UtcNow
            });
        }
    }
}
