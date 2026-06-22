using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        public string? TicketCode { get; set; }
        public int? SessionId { get; set; }
        public string? CheckoutLicensePlate { get; set; }
        public string PaymentMethod { get; set; } = "CASH";
        public IFormFile? ImageFile { get; set; }

        // THÊM THUỘC TÍNH NÀY
        public string? ImageUrl { get; set; }
    }
}