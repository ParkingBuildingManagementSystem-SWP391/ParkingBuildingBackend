using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class BookSlotRequest
    {
        public int UserId { get; set; }
        public int SlotId { get; set; }
        public string LicenseVehicle { get; set; } = string.Empty;
        public int TypeId { get; set; }
    }
}
