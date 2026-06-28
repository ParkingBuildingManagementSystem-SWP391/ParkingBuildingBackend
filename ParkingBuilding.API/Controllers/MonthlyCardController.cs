using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System.Security.Claims;

namespace ParkingBuilding.API.Controllers
{
    [Authorize(Roles = "Registered_Driver")]
    [ApiController]
    [Route("api/[controller]")]
    public class MonthlyCardController : ControllerBase
    {
        private readonly IMonthlyCardService _monthlyCardService;

        public MonthlyCardController(IMonthlyCardService monthlyCardService)
        {
            _monthlyCardService = monthlyCardService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterMonthlyCardDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");
            }

            int currentUserId = int.Parse(userIdClaim);
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            try
            {
                var result = await _monthlyCardService.RegisterMonthlyCardAsync(currentUserId, request, ipAddress);
                return Ok(new { isSuccess = true, data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}
