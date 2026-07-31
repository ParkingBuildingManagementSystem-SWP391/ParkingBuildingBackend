using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        public string? TicketCode { get; set; }
        public int? SessionId { get; set; }
        public string? CheckoutLicensePlate { get; set; }
        public string PaymentMethod { get; set; } = "CASH"; // "CASH", "VNPAY", "WALLET" hoac "AUTO"
        public IFormFile? ImageFile { get; set; }

        // Tên phải khớp với field "checkOutImageUrl" mà FE gửi (field-name binding phân biệt tên, không chỉ hoa/thường).
        public string? CheckOutImageUrl { get; set; }

        /// <summary>
        /// Nhân viên xác nhận bỏ qua cảnh báo bảo mật (biển số / mã vé không khớp) để cho xe ra.
        /// Mặc định = false (hệ thống sẽ chặn nếu phát hiện bất thường).
        /// Khi = true: hệ thống vẫn lưu IncidentReport nhưng tiếp tục xử lý checkout.
        /// </summary>
        public bool OverrideSecurityCheck { get; set; } = false;
    }
}
