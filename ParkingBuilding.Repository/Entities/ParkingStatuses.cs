using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Entities
{
    public static class ParkingStatuses
    {
        // Trạng thái ô đỗ (ParkingSlot.SlotStatus)
        public const string SlotAvailable = "Available"; // Ô trống sẵn sàng
        public const string SlotReserved = "Reserved";   // Khách đã đặt trên mạng
        public const string SlotOccupied = "Occupied";   // Xe đã đỗ thực tế

        // Trạng thái lượt đỗ (ParkingSession.SessionStatus)
        public const string SessionReserved = "Reserved";     // Chờ xe đến check-in
        public const string SessionInProgress = "InProgress"; // Xe đang trong bãi
        public const string SessionCompleted = "Completed";   // Xe đã thanh toán và ra
        public const string SessionCanceled = "Canceled";     // Quá 15p bùng hẹn bị hủy

        // Trạng thái vé (Ticket.TicketStatus)
        public const string TicketActive = "Active";       // Vé hoạt động
        public const string TicketExpired = "Expired";     // Vé hết hiệu lực do bùng hẹn
        public const string TicketCompleted = "Completed"; // Vé đã thanh toán/check-out xong

        // Trạng thái thẻ tháng (MonthlyCard.Status)
        public const string MonthlyCardActive = "Active";       // Thẻ hoạt động bình thường
        public const string MonthlyCardExpired = "Expired";     // Thẻ đã hết hạn
        public const string MonthlyCardSuspended = "Suspended"; // Thẻ bị tạm khóa
    }
}
