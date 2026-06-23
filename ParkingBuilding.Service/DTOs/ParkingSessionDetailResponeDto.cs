using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class ParkingSessionDetailResponeDto
    {
        public string? Username { get; set; }
        public string SlotName { get; set; } = null!;
        public string LicenseVehicle { get; set; } = null!;
        public int TypeId { get; set; }
        public DateTime? BookingTime { get; set; }
        public DateTime? CheckInTime { get; set; }
        public DateTime? CheckOutTime { get; set; }
        public string? CheckInImageUrl { get; set; }
        public string? CheckOutImageUrl { get; set; }
        public string SessionStatus { get; set; } = null!;
    }
}
