using System;

namespace ParkingBuilding.Service.DTOs
{
    public class ActiveSessionResponseDto
    {
        public int SessionId { get; set; }
        public string LicenseVehicle { get; set; } = null!;
        public string? TicketCode { get; set; }
        public string SessionStatus { get; set; } = null!;
        public string VehicleTypeName { get; set; } = null!;
        public int SlotId { get; set; }
        public string SlotName { get; set; } = null!;
    }
}
