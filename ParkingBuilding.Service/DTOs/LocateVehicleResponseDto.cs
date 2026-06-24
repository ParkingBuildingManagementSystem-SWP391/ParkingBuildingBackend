using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class LocateVehicleResponseDto
    {
        public string LicenseVehicle { get; set; } = null!;
        public string SlotName { get; set; } = null!;
        public string FloorName { get; set; } = null!;
        public int FloorId { get; set; }
        public DateTime CheckInTime { get; set; }
        public string? CheckInImageUrl { get; set; }
    }
}
