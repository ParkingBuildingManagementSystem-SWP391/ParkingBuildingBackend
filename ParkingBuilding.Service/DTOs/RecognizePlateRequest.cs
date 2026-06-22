using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.DTOs
{
    public class RecognizePlateRequest
    {
        public IFormFile ImageFile { get; set; }
        public int VehicleTypeId { get; set; }
    }
}
