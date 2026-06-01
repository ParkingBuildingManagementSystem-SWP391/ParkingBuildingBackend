using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInRequest
    {
        public string LicenseVehicle { get; set; } = null!;
        public int TypeId { get; set; }
        public string? CardOrTicketId { get; set; }
        public string? CheckInImageUrl { get; set; }
    }
}
