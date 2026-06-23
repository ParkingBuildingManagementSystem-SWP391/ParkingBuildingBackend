using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class ParkingSessionResponeDto
    {       
        public string SlotName { get; set; } = null!;
        public int TypeId { get; set; }
        public string? TicketCode { get; set; }
        public string SessionStatus { get; set; } = null!;
    }
}
