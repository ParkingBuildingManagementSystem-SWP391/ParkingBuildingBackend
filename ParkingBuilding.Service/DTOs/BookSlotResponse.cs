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
        public string SlotName { get; set; } = string.Empty;        
        
        public DateTime? BookingTime { get; set; }  
        
        public string QrCodeBase64 { get; set; } = string.Empty;

        public bool RequiresPayment { get; set; } // Trả về true nếu trên 2 tiếng
        public string PaymentUrl { get; set; } = string.Empty; // Đường link dẫn tới cổng VNPay
        public int? InvoiceId { get; set; }
    }
}
