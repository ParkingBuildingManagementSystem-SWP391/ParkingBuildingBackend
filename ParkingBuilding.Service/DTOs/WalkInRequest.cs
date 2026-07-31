using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInRequest
    {
        public string? LicenseVehicle { get; set; }
        public int VehicleTypeId { get; set; }
        public IFormFile? ImageFile { get; set; }

        // Tên phải khớp với field "checkInImageUrl" mà FE gửi.
        public string? CheckInImageUrl { get; set; }
    }
}