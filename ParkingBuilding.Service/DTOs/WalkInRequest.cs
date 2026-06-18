using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInRequest
    {
        [Required]
        [DefaultValue("")]
        public string LicenseVehicle { get; set; } = null!;
        [Required]
        public int VehicleTypeId { get; set; }
        [Required]
        public string? CheckInImageUrl { get; set; }        
    }
}
