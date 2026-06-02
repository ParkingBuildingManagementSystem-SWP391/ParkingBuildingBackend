using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System;

namespace ParkingBuilding.Service.DTOs
{
    public class BookSlotResponse
    {
        public bool IsSuccess { get; set; }
        
        public string Message { get; set; } = string.Empty;
        public string TicketCode { get; set; } = string.Empty;
        public string SlotId { get; set; } = string.Empty;        
        
        public DateTime? BookingTime { get; set; }  
        
        public string QrCodeBase64 { get; set; } = string.Empty;
    }
}
