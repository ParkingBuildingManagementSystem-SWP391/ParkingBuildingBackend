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

        public ParkingController(IParkingService parkingService)
        {
            _parkingService = parkingService;
        }

        // API 1: Khách đặt chỗ trước (Khớp 100% code gốc của bạn)
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

        // API 2: Quét cổng vào check-in (Khớp 100% code gốc của bạn)
        [HttpPost("check-in")]
        public async Task<IActionResult> CheckInVehicle([FromBody] CheckInRequest request)
        {
            try
            {
                var isSuccess = await _parkingService.CheckInVehicleAsync(request);
                if (isSuccess) return Ok(new { message = "Check-in thành công! Mời xe tiến qua thanh chắn vào bãi." });
                return BadRequest("Check-in thất bại. Không tìm thấy lịch trình đặt chỗ của xe này, hoặc đơn đặt chỗ đã quá hạn 15 phút nên hệ thống tự động hủy.");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // API 3: Khách vãng lai đến cổng (Walk-in)
        [HttpPost("walk-in")]
        public async Task<IActionResult> WalkInCheckIn([FromBody] WalkInRequest request)
        {
            try
            {
                var result = await _parkingService.WalkInCheckInAsync(request);
                return Ok(new
                {
                    message = $"Check-in khách vãng lai thành công! Xe đỗ tại vị trí: {result.SlotName}.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
