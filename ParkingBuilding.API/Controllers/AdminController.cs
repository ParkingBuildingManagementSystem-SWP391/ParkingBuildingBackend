using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;

namespace ParkingBuilding.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;

        public AdminController(IAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet("users")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                var users = await _adminService.GetAllUsersAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi lấy danh sách: " + ex.Message });
            }
        }

        [HttpPost("update-user")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateUser([FromBody] UpdateUserRequestDto request)
        {
            try
            {
                var result = await _adminService.updateUserAsync(request);
                if (result)
                {
                    return Ok(new { message = $"Cập nhật thành công! Tài khoản ID {request.UserId} ." });
                }
                return BadRequest(new { error = "Ủy quyền thất bại. Không thể lưu thay đổi vào cơ sở dữ liệu." });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi phân quyền: " + ex.Message });
            }
        }

        [HttpPost("create-user")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequestDto request)
        {
            try
            {
                var result = await _adminService.CreateUserAsync(request);
                return CreatedAtAction(nameof(GetAllUsers), new { id = result.Id }, new { message = "Tạo tài khoản người dùng thành công!", data = result });
            }
            catch (ArgumentException ex)
            {
                // Lỗi trùng lặp email hoặc tham số không hợp lệ
                return BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                // Lỗi không tìm thấy Role
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                // Lỗi hệ thống ngoài ý muốn
                return StatusCode(500, new { error = "Lỗi hệ thống khi tạo người dùng: " + ex.Message });
            }
        }
    }
}
