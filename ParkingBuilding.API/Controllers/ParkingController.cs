using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;


namespace ParkingBuilding.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ParkingController : ControllerBase
    {
        private readonly IParkingService _parkingService;

        public ParkingController(IParkingService parkingService) { _parkingService = parkingService; }

        [HttpPost("book")]
        public async Task<IActionResult> BookSlot([FromBody] BookSlotRequest request)
        {
            try
            {
                var isSuccess = await _parkingService.BookSlotAsync(request);
                if (isSuccess) return Ok(new { message = "Đặt chỗ thành công! Bạn có 15 phút để check-in." });
                return BadRequest("Đặt chỗ thất bại.");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
