using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class ParkingSlotResponseDto
    {
        public int SlotId { get; set; }
        public string SlotName { get; set; } = null!;
        public string SlotStatus { get; set; } = null!;
        public int TypeId { get; set; }
    }
}

