using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckInRequest
    {
        public string LicenseVehicle { get; set; } = null!;
        public string? CheckInImageUrl { get; set; }
    }
}