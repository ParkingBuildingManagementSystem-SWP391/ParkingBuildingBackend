using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Hubs
{
    /// <summary>
    /// Hub SignalR xử lý các kết nối thời gian thực dành riêng cho Tài xế.
    /// Yêu cầu xác thực JWT Token và chỉ cho phép người dùng có Role "Registered_Driver" kết nối.
    /// </summary>
    [Authorize(Roles = "Registered_Driver")]
    public class DriverNotificationHub : Hub
    {
        private readonly ILogger<DriverNotificationHub> _logger;

        public DriverNotificationHub(ILogger<DriverNotificationHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Kích hoạt khi một tài xế kết nối thành công tới Hub.
        /// Tự động thêm tài xế vào nhóm chung của tất cả các tài xế (nếu cần gửi thông báo toàn bộ tài xế).
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = Context.UserIdentifier;
            _logger.LogInformation("Tài xế kết nối thành công: ConnectionId = {ConnectionId}, UserId = {UserId}", Context.ConnectionId, userId);

            // Thêm vào nhóm chung của tất cả Driver
            await Groups.AddToGroupAsync(Context.ConnectionId, "All_Drivers");

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Kích hoạt khi tài xế ngắt kết nối.
        /// SignalR tự động dọn dẹp các nhóm mà ConnectionId này đang tham gia, 
        /// nhưng ta có thể ghi nhận log để theo dõi.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.UserIdentifier;
            if (exception != null)
            {
                _logger.LogWarning("Tài xế ngắt kết nối với lỗi: UserId = {UserId}, Lỗi = {Message}", userId, exception.Message);
            }
            else
            {
                _logger.LogInformation("Tài xế ngắt kết nối chủ động: UserId = {UserId}", userId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Cho phép tài xế chủ động đăng ký tham gia một nhóm cụ thể.
        /// Ví dụ: Đăng ký nhận thông tin về bãi đỗ xe hoặc ca đỗ xe nhất định: groupName = "shift-parking-price-building-A"
        /// </summary>
        public async Task JoinGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new HubException("Tên nhóm không hợp lệ.");

            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Tài xế {UserId} đã tham gia nhóm: {GroupName}", Context.UserIdentifier, groupName);
            
            // Gửi xác nhận về cho chính Client vừa đăng ký
            await Clients.Caller.SendAsync("JoinedGroup", groupName);
        }

        /// <summary>
        /// Cho phép tài xế rời khỏi một nhóm cụ thể khi không còn quan tâm nữa.
        /// </summary>
        public async Task LeaveGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new HubException("Tên nhóm không hợp lệ.");

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
            _logger.LogInformation("Tài xế {UserId} đã rời khỏi nhóm: {GroupName}", Context.UserIdentifier, groupName);

            // Gửi xác nhận về cho chính Client vừa rời nhóm
            await Clients.Caller.SendAsync("LeftGroup", groupName);
        }
    }
}
