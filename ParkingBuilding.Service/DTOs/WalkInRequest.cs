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
        public int VehicleTypeId { get; set; }               
        public string? CheckInImageUrl { get; set; }        
    }
}
