using ParkingBuilding.Repository.Entities;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    /// <summary>
    /// Giao diện trừu tượng cho dịch vụ thông báo (Notification Service).
    /// Đóng vai trò là cầu nối giữa BLL và SignalR Hub ở Presentation layer,
    /// tránh phụ thuộc vòng (circular dependency) trực tiếp của BLL vào SignalR Hub.
    /// </summary>
    public interface INotificationService
    {
        /// <summary>
        /// Tạo và gửi thông báo đích danh tới một tài xế cụ thể.
        /// Thích hợp cho thông báo biến động số dư ví (Wallet Transactions).
        /// </summary>
        Task<Notification> SendToUserAsync(int userId, string title, string content, string type);

        /// <summary>
        /// Tạo và gửi thông báo tới một nhóm đối tượng tài xế đang quan tâm/đăng ký một group.
        /// Thích hợp cho thông báo diện rộng: thay đổi giá Booking, giá ca đỗ xe, giá thẻ thành viên.
        /// </summary>
        Task SendToGroupAsync(string groupName, string title, string content, string type);
    }

    /// <summary>
    /// Định nghĩa các loại thông báo (Notification Types) được quy định trong hệ thống.
    /// </summary>
    public static class NotificationTypes
    {
        public const string BookingPriceUpdate = "BookingPriceUpdate";
        public const string ShiftParkingPriceUpdate = "ShiftParkingPriceUpdate";
        public const string MembershipPriceUpdate = "MembershipPriceUpdate";
        public const string WalletTransaction = "WalletTransaction";
        public const string IncidentResolved = "IncidentResolved";
        public const string IncidentCreated = "IncidentCreated";
    }
}
