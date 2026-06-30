using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System.Security.Claims;

namespace ParkingBuilding.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    /// <summary>
    /// API Controller quản lý xác thực và phân quyền (Authentication).
    /// Cho phép đăng ký bằng Email OTP, xác thực mã OTP, đăng nhập thường và đăng nhập bên thứ 3 bằng tài khoản Google.
    /// </summary>
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        /// <summary>
        /// API Đăng ký tài khoản (Bước 1):
        /// - Nhận thông tin Email, Mật khẩu, băm mật khẩu, sinh OTP ngẫu nhiên, lưu cache 5 phút và gửi OTP về hòm thư người dùng.
        /// </summary>
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            try
            {
                var result = await _authService.RegisterAsync(request);
                return Ok(new { message = result });
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi đăng ký: " + ex.Message });
            }
        }

        [HttpPost("verify-otp")]
        /// <summary>
        /// API xác thực OTP (Bước 2):
        /// - Nhận OTP từ tài xế, nếu khớp với dữ liệu tạm thời trong cache thì lưu tài khoản chính thức vào DB dưới vai trò 'Registered_Driver' và trả về JWT Token.
        /// </summary>
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpRequest request)
        {
            try
            {
                var response = await _authService.VerifyOtpAsync(request);
                return Ok(response);
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi xác thực OTP: " + ex.Message });
            }
        }

        [HttpPost("login")]
        /// <summary>
        /// API Đăng nhập bằng tài khoản và mật khẩu truyền thống.
        /// - Kiểm tra mật khẩu bằng BCrypt, sinh JWT Token chứa ID và Role trả về cho Client.
        /// </summary>
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            
            try
            {
                string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
                var response = await _authService.LoginAsync(request, ipAddress);
                return Ok(response);
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi đăng nhập: " + ex.Message });
            }
        }

        [HttpPost("google")]
        /// <summary>
        /// API Đăng nhập bằng Google ID Token (Single Sign-On).
        /// - Xác minh token với Google API, nếu email chưa tồn tại trong hệ thống thì tự động đăng ký tài khoản với Password rỗng.
        /// </summary>
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            try
            {
                var response = await _authService.ContinueWithGoogleAsync(request);
                return Ok(response);
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống xác thực Google: " + ex.Message });
            }
        }

        [HttpPost("forgot-password")]
        /// <summary>
        /// API yêu cầu quên mật khẩu: Sinh OTP và gửi qua Email
        /// </summary>
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            try
            {
                await _authService.ForgotPasswordAsync(request);
                return Ok(new { message = "Mã OTP đã được gửi đến email của bạn. Vui lòng kiểm tra hộp thư!" });
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi gửi yêu cầu quên mật khẩu: " + ex.Message });
            }
        }

        [HttpPost("reset-password")]
        /// <summary>
        /// API đặt lại mật khẩu sử dụng mã OTP xác thực
        /// </summary>
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                await _authService.ResetPasswordAsync(request);
                return Ok(new { message = "Mật khẩu của bạn đã được đặt lại thành công! Vui lòng đăng nhập lại." });
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi đặt lại mật khẩu: " + ex.Message });
            }
        }

        [Authorize]
        [HttpPut("update-profile")]
        /// <summary>
        /// API người dùng đăng nhập tự cập nhật thông tin cá nhân và đổi mật khẩu
        /// </summary>
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            try
            {
                // Lấy UserId từ JWT Claims
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized("Không thể xác định danh tính tài khoản.");
                }

                int userId = int.Parse(userIdClaim);
                await _authService.UpdateProfileAsync(userId, request);

                return Ok(new { message = "Cập nhật hồ sơ cá nhân thành công!" });
            }
            catch (BadHttpRequestException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Lỗi hệ thống khi cập nhật hồ sơ: " + ex.Message });
            }
        }

    }
}