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

        // THÊM THUỘC TÍNH NÀY: Nhận URL từ Frontend gửi lên ở bước xác nhận
        public string? ImageUrl { get; set; }
    }
}