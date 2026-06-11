using System;

namespace ParkingBuilding.Service.DTOs
{
    public class SlotDetailResponse
    {
        public int SlotId { get; set; }
        public string SlotName { get; set; } = string.Empty;
        public string SlotStatus { get; set; } = string.Empty; // "Available", "Reserved", "Occupied"
        public string FloorName { get; set; } = string.Empty;

        public ActiveSessionDto? ActiveSession { get; set; }
    }

    public class ActiveSessionDto
    {
        public int SessionId { get; set; }
        public string LicenseVehicle { get; set; } = string.Empty;
        public string VehicleTypeName { get; set; } = string.Empty;
        public string SessionStatus { get; set; } = string.Empty; // "Reserved", "InProgress"
        public DateTime? BookingTime { get; set; }
        public DateTime? CheckInTime { get; set; }

        public CustomerInfoDto? Customer { get; set; }
    }

    public class CustomerInfoDto
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string CustomerType { get; set; } = "Đăng ký thành viên";
    }
}
