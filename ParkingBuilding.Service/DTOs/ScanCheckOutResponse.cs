using System;

namespace ParkingBuilding.Service.DTOs
{
    public class ScanCheckOutResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = null!;
        public int SessionId { get; set; }
        public string TicketCode { get; set; } = null!;
        public string SlotName { get; set; } = null!;
        public string CheckInLicensePlate { get; set; } = null!;
        public string? CheckInImageUrl { get; set; }
        public DateTime CheckInTime { get; set; }
        public DateTime CheckOutTime { get; set; }
        public double DurationHours { get; set; }
        public decimal TotalAmount { get; set; }
        public bool IsPaid { get; set; }
        public string? PaymentStatus { get; set; }
        public string? PaymentMethod { get; set; }
        
        // Driver / User Info
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? DriverEmail { get; set; }
        public string CustomerType { get; set; } = null!; // "Booking" or "WalkIn"
        public string VehicleTypeName { get; set; } = null!;
    }
}
