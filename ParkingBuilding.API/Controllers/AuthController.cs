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
    }
}