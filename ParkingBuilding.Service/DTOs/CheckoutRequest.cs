using Microsoft.AspNetCore.Http; // Thêm thư viện này

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        public string? TicketCode { get; set; }
        public int? SessionId { get; set; }
        public string? CheckoutLicensePlate { get; set; }
        public string PaymentMethod { get; set; } = "CASH";

        // THÊM: Nhận file ảnh từ React FormData
        public IFormFile? ImageFile { get; set; }
    }
}
