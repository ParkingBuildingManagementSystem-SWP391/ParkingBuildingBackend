using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;

// jwt 
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;


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

        // API 1: Khách đặt chỗ trước 
        [Authorize]
        [HttpPost("book")]
        [Authorize(Roles = "Registered_Driver")]
        public async Task<IActionResult> BookSlot([FromBody] BookSlotRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin User trong Token." });
                }

                int userId = int.Parse(userIdClaim);

                BookSlotResponse response = await _parkingService.BookSlotAsync(userId, request);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        // =========================================================================
        // API 2: QUÉT CỔNG VÀO CHECK-IN (KHỚP 100% CODE GỐC CHUẨN CỦA BẠN)
        // =========================================================================
        [Authorize(Roles = "Staff")] 
        [HttpPost("check-in")]
        public async Task<IActionResult> CheckInVehicle([FromBody] CheckInRequest request)
        {
            try
            {
                // Gọi xuống Service kiểm tra biển số xe và mốc thời gian 15 phút
                var isSuccess = await _parkingService.CheckInVehicleAsync(request);

                if (isSuccess)
                    return Ok(new { message = "Check-in thành công! Mời xe tiến qua thanh chắn vào bãi." });

                return BadRequest("Check-in thất bại. Không tìm thấy lịch trình đặt chỗ của xe này, hoặc đơn đặt chỗ đã quá hạn 15 phút nên hệ thống tự động hủy.");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // API 3: Khách vãng lai đến cổng (Walk-in)
        [Authorize(Roles = "Staff")]
        [HttpPost("walk-in")]
        public async Task<IActionResult> WalkInCheckIn([FromBody] WalkInRequest request)
        {
            try
            {
                var result = await _parkingService.WalkInCheckInAsync(request);
                return Ok(new
                {
                    message = $"Check-in khách hàng thành công! Xe đỗ tại vị trí: {result.SlotName}.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpGet("floor/{floorId}")]
        public async Task<IActionResult> GetSlotsByFloorId(int floorId)
        {
            try
            {
                var slots = await _parkingService.GetSlotsByFloorIdAsync(floorId);
                return Ok(slots);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }
    }
}
