using System;

namespace ParkingBuilding.Service.DTOs
{
    public class MyBookingResponseDto
    {
        public int SessionId { get; set; }
        public int TypeId { get; set; }
        public DateTime? BookingTime { get; set; }
        public string SessionStatus { get; set; } = string.Empty;
        public string FloorName { get; set; } = string.Empty;
        public string SlotName { get; set; } = string.Empty;
        public string LicenseVehicle { get; set; } = string.Empty;
        public string? TicketCode { get; set; }

        // Thời gian thực tế xe vào/ra bãi
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }

        // Thông tin hóa đơn thanh toán
        public decimal? TotalAmount { get; set; }
        public string? PaymentStatus { get; set; }
        public string? PaymentMethod { get; set; }
    }
}
