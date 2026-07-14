using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StaffController : ControllerBase
    {
        private readonly IStaffLogService _staffLogService;

        public StaffController(IStaffLogService staffLogService)
        {
            _staffLogService = staffLogService;
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("shift/start")]
        public async Task<IActionResult> StartShift()
        {
            try
            {
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin nhân viên." });

                int staffId = int.Parse(staffIdClaim);
                var response = await _staffLogService.StartShiftAsync(staffId);
                return Ok(new { isSuccess = true, message = "Mở ca trực thành công.", data = response });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("shift/end")]
        public async Task<IActionResult> EndShift([FromBody] EndShiftRequest request)
        {
            try
            {
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin nhân viên." });

                int staffId = int.Parse(staffIdClaim);
                var response = await _staffLogService.EndShiftAsync(staffId, request);
                return Ok(new { isSuccess = true, message = "Đóng ca trực thành công.", data = response });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("shift/active")]
        public async Task<IActionResult> GetActiveShift()
        {
            try
            {
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin nhân viên." });

                int staffId = int.Parse(staffIdClaim);
                var response = await _staffLogService.GetActiveShiftAsync(staffId);

                if (response == null)
                {
                    return Ok(new { isSuccess = true, hasActiveShift = false, message = "Bạn hiện không có ca trực nào đang hoạt động." });
                }

                return Ok(new { isSuccess = true, hasActiveShift = true, data = response });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Đã xảy ra lỗi hệ thống.", error = ex.Message });
            }
        }
    }
}
