using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInResponse
    {
        public int SessionId { get; set; }
        public string TicketCode { get; set; } = null!;
        public string SlotName { get; set; } = null!;
        public string Status { get; set; } = null!;
    }
}
