using Microsoft.AspNetCore.Http; // Thêm thư viện này

namespace ParkingBuilding.Service.DTOs
{
    public class WalkInRequest
    {
        public string? LicenseVehicle { get; set; }
        public int VehicleTypeId { get; set; }

        // THÊM: Nhận file ảnh từ React FormData
        public IFormFile? ImageFile { get; set; }
    }
}
