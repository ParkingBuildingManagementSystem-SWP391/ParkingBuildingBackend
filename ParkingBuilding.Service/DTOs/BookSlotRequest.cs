using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class BookSlotRequest
    {
        public int SlotId { get; set; }
        public string LicenseVehicle { get; set; } = null!;
        public int TypeId { get; set; }
    }
}
