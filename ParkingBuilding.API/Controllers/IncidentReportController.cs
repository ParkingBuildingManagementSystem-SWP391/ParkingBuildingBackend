using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [ApiController]
    [Route("api/incident-reports")]
    public class IncidentReportController : ControllerBase
    {
        private readonly IIncidentReportService _incidentService;

        public IncidentReportController(IIncidentReportService incidentService)
        {
            _incidentService = incidentService;
        }

        // 1. Lấy danh sách báo cáo sự cố (Có lọc)
        [Authorize(Roles = "Staff,Manager,Admin")]
        [HttpGet]
        public async Task<IActionResult> GetIncidents(
            [FromQuery] string? status, 
            [FromQuery] string? issueType, 
            [FromQuery] string? licenseVehicle)
        {
            var result = await _incidentService.GetIncidentsAsync(status, issueType, licenseVehicle);
            return Ok(result);
        }

        // 2. Lấy chi tiết báo cáo sự cố theo ID
        [Authorize(Roles = "Staff,Manager,Admin")]
        [HttpGet("{id}")]
        public async Task<IActionResult> GetIncidentDetail(int id)
        {
            var result = await _incidentService.GetIncidentByIdAsync(id);
            if (result == null) return NotFound(new { message = "Không tìm thấy báo cáo sự cố." });
            return Ok(result);
        }

        // 3. Nhân viên hoặc Khách hàng báo cáo sự cố mới
        [Authorize(Roles = "Staff,Registered_Driver")]
        [HttpPost]
        public async Task<IActionResult> CreateIncident([FromBody] CreateIncidentReportDto dto)
        {
            // Lấy UserId của tài khoản đang đăng nhập từ JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { message = "Token không chứa ID tài khoản hợp lệ." });
            }

            try
            {
                var result = await _incidentService.CreateIncidentAsync(dto, userId);
                return CreatedAtAction(nameof(GetIncidentDetail), new { id = result.IncidentId }, result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
            }
        }

        // 4. Giải quyết sự cố (Chỉ dành cho Manager)
        [Authorize(Roles = "Manager")]
        [HttpPut("{id}/resolve")]
        public async Task<IActionResult> ResolveIncident(int id, [FromBody] ResolveIncidentReportDto dto)
        {
            // Lấy UserId của người xử lý từ JWT Token
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized(new { message = "Token không chứa ID tài khoản hợp lệ." });
            }

            var success = await _incidentService.ResolveIncidentAsync(id, dto, userId);
            if (!success)
            {
                return BadRequest(new { message = "Không thể giải quyết sự cố này. Có thể do sự cố đã được đóng hoặc ID không tồn tại." });
            }

            return Ok(new { message = "Đã cập nhật trạng thái giải quyết sự cố thành công." });
        }
    }
}
