using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class PaymentResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public long? InvoiceId { get; set; }
        public string ErrorCode { get; set; } // Dùng để phản hồi mã lỗi VNPay (ví dụ: "00", "01", "02", "97")
        public string PaymentUrl { get; set; } // Chứa URL thanh toán VNPay trả về cho Client
        
        public decimal? ChangeDue { get; set; } // Tiền thừa trả khách
        public string? LicenseVehicle { get; set; } // Biển số xe của phiên đỗ
        public string? SlotName { get; set; } // Tên ô đỗ được giải phóng
    }
}
