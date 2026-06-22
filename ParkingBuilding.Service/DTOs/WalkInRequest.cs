using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInRequest
    {
        public string? LicenseVehicle { get; set; }
        public int VehicleTypeId { get; set; }
        public IFormFile? ImageFile { get; set; }

        // THÊM THUỘC TÍNH NÀY
        public string? ImageUrl { get; set; }
    }
}