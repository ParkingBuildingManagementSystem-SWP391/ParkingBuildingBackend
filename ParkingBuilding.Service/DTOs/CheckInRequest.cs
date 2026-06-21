using Microsoft.AspNetCore.Http; // Thêm thư viện này
using System.ComponentModel;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckInRequest
    {
        [DefaultValue("")]
        public string? LicenseVehicle { get; set; }

        [DefaultValue("")]
        public string? TicketCode { get; set; }


        // THÊM: Nhận file ảnh từ React FormData
        public IFormFile? ImageFile { get; set; }
    }
}
