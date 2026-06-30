using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using Microsoft.Extensions.Logging;

namespace ParkingBuilding.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// API Controller dành cho Quản trị viên (Admin).
    /// Hỗ trợ các tác vụ quản lý tài khoản người dùng, tạo tài khoản Staff/Driver và phân quyền hệ thống.
    /// </summary>
    public class AdminController : ControllerBase
    {
        private readonly IAdminService _adminService;
        private readonly ILogger<AdminController> _logger;

        public AdminController(IAdminService adminService, ILogger<AdminController> logger)
        {
            _adminService = adminService;
            _logger = logger;        }

        [HttpGet("users")]
        /// <summary>
        /// API lấy danh sách toàn bộ tài khoản người dùng hoạt động trong hệ thống.
        /// - Yêu cầu vai trò Admin.
        /// </summary>
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

        [HttpPut("update-user")]
        /// <summary>
        /// API cập nhật thông tin cá nhân và thay đổi phân quyền (Role) của người dùng.
        /// - Yêu cầu vai trò Admin.
        /// </summary>
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
        /// <summary>
        /// API để Admin trực tiếp khởi tạo tài khoản mới (Staff/Driver) không qua luồng xác thực OTP.
        /// - Yêu cầu vai trò Admin.
        /// </summary>
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

        [HttpGet("sessions")]
        /// <summary>
        /// API 1: Lấy danh sách toàn bộ các phiên đỗ xe hiện có (Không điều kiện).
        /// - Quyền truy cập: Chỉ Admin.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllParkingSessions()
        {
            // Ghi nhận nhật ký bắt đầu gọi API
            _logger.LogInformation("Admin requested GetAllParkingSessions - Lấy danh sách phiên đỗ xe.");
            try
            {
                var sessions = await _adminService.GetAllParkingSessionsAsync();
                _logger.LogInformation("Successfully retrieved {Count} parking sessions.", sessions.Count);
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong GetAllParkingSessions: {Message}", ex.Message);
                return StatusCode(500, new { error = "Lỗi hệ thống khi lấy danh sách phiên đỗ: " + ex.Message });
            }
        }

        [HttpGet("sessions/search")]
        /// <summary>
        /// API 2: Tìm kiếm và lọc danh sách các phiên đỗ xe theo nhiều tiêu chí.
        /// - Tiêu chí lọc: Biển số, Tên ô đỗ, Tên tài xế, Loại xe, Trạng thái, Khoảng thời gian.
        /// - Quyền truy cập: Chỉ Admin.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetParkingSessionsWithFilters(
            [FromQuery] string? licenseVehicle,
            [FromQuery] string? slotName,
            [FromQuery] int? isRegistered,
            [FromQuery] int? typeId,
            [FromQuery] string? sessionStatus,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate)
        {
            _logger.LogInformation("Admin requested GetParkingSessionsWithFilters: licenseVehicle={LicenseVehicle}, slotName={SlotName}, isRegistered={IsRegistered}, typeId={TypeId}, sessionStatus={SessionStatus}, fromDate={FromDate}, toDate={ToDate}",
                licenseVehicle, slotName, isRegistered, typeId, sessionStatus, fromDate, toDate);
            try
            {
                var sessions = await _adminService.GetParkingSessionsWithFiltersAsync(
                    licenseVehicle, slotName, isRegistered, typeId, sessionStatus, fromDate, toDate);
                _logger.LogInformation("Successfully retrieved {Count} filtered parking sessions.", sessions.Count);
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong GetParkingSessionsWithFilters với bộ lọc.");
                return StatusCode(500, new { error = "Lỗi hệ thống khi tìm kiếm phiên đỗ xe: " + ex.Message });
            }
        }

        [HttpGet("sessions/by-ticket/{ticketCode}")]
        /// <summary>
        /// API 3: Truy vết chi tiết phiên đỗ xe dựa theo mã vé (TicketCode).
        /// - Quyền truy cập: Chỉ Admin.
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetSessionDetailByTicketCode(string ticketCode)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ticketCode))
                {
                    return BadRequest(new { error = "Vui lòng cung cấp mã vé (TicketCode) hợp lệ!" });
                }

                var sessionDetail = await _adminService.GetSessionDetailByTicketCodeAsync(ticketCode);
                if (sessionDetail == null)
                {
                    return NotFound(new { error = $"Không tìm thấy phiên đỗ xe nào có TicketCode: '{ticketCode}'" });
                }

                return Ok(sessionDetail);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi lấy chi tiết phiên đỗ xe: " + ex.Message });
            }
        }
    }
}
