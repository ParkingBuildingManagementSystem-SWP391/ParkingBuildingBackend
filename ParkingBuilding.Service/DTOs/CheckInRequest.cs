using Microsoft.AspNetCore.Http;
using System.ComponentModel;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckInRequest
    {
        [DefaultValue("")]
        public string? LicenseVehicle { get; set; }

        [DefaultValue("")]
        public string? TicketCode { get; set; }

        public IFormFile? ImageFile { get; set; }

        // Nhận URL ảnh từ Frontend gửi lên ở bước xác nhận.
        // Tên phải khớp với field "checkInImageUrl" mà FE gửi (JSON/Form field-name binding phân biệt tên, không chỉ hoa/thường).
        public string? CheckInImageUrl { get; set; }
    }
}