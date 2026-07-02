using System;

namespace ParkingBuilding.Service.DTOs
{
    public class ScanCheckInResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = null!;
        public int SessionId { get; set; }
        public string LicenseVehicle { get; set; } = null!;
        public string SlotName { get; set; } = null!;
        public string VehicleTypeName { get; set; } = null!;
        public DateTime? ExpectedCheckInTime { get; set; }
        public DateTime? BookingTime { get; set; }
        
        // Driver / User Info
        public string? DriverName { get; set; }
        public string? DriverPhone { get; set; }
        public string? DriverEmail { get; set; }
        
        public string TicketCode { get; set; } = null!;
        public bool RequiresPayment { get; set; }
        public string? PaymentStatus { get; set; }
        public bool RequiresWalkIn { get; set; }
    }
}
