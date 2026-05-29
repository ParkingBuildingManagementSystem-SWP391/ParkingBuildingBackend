using Google.Apis.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.Service
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly ITokenService _tokenService;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;

        public AuthService(
            IUserRepository userRepository,
            ITokenService tokenService,
            IEmailService emailService,
            IMemoryCache cache,
            IConfiguration config)
        {
            _userRepository = userRepository;
            _tokenService = tokenService;
            _emailService = emailService;
            _cache = cache;
            _config = config;
        }

        public async Task<string> RegisterAsync(RegisterRequest request)
        {
            // 1. Kiểm tra Email trùng
            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                throw new BadHttpRequestException("Email này đã được sử dụng bởi tài khoản khác!");
            }

            // 2. Hash mật khẩu bằng BCrypt
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // 3. Sinh OTP 6 số ngẫu nhiên
            var otp = new Random().Next(100000, 999999).ToString();

            // 4. Lưu tạm vào Cache trong 5 phút
            var tempData = new RegisterTempData
            {
                Email = request.Email,
                PasswordHash = passwordHash,
                Username = request.Username,
                OtpCode = otp
            };

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            _cache.Set(request.Email, tempData, cacheOptions);

            // 5. Gửi Mail OTP
            var mailSubject = "Mã xác thực đăng ký tài khoản Parking Building";
            var mailBody = $@"
            <h3>Chào mừng {request.Username}!</h3>
            <p>Cảm ơn bạn đã đăng ký tài khoản. Mã OTP xác thực của bạn là:</p>
            <h2 style='color: #4c8bf5; letter-spacing: 2px;'>{otp}</h2>
            <p>Mã này có hiệu lực trong vòng 5 phút. Vui lòng không chia sẻ mã này với bất kỳ ai.</p>
        ";

            await _emailService.SendEmailAsync(request.Email, mailSubject, mailBody);

            return "Mã OTP đã được gửi qua Email của bạn. Vui lòng xác thực để hoàn tất!";
        }


        public async Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request)
        {
            // 1. Lấy dữ liệu tạm từ Cache
            if (!_cache.TryGetValue(request.Email, out RegisterTempData? tempData) || tempData == null)
            {
                throw new BadHttpRequestException("Yêu cầu đăng ký đã hết hạn hoặc không tồn tại. Vui lòng đăng ký lại!");
            }

            // 2. Kiểm tra mã OTP
            if (tempData.OtpCode != request.OtpCode)
            {
                throw new BadHttpRequestException("Mã OTP không chính xác. Vui lòng thử lại!");
            }

            // 3. Lấy Role mặc định
            var defaultRole = await _userRepository.GetRoleByNameAsync("Member");
            if (defaultRole == null)
            {
                throw new Exception("Hệ thống chưa thiết lập Role 'Member' mặc định.");
            }

            // 4. Tạo User lưu DB
            var user = new User
            {
                Email = tempData.Email,
                Username = tempData.Username,
                PasswordHash = tempData.PasswordHash,
                RoleId = defaultRole.RoleId,
                IsDeleted = false
            };

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            // 5. Xóa bỏ cache sau khi dùng
            _cache.Remove(request.Email);

            // 6. Tải lại user kèm role để sinh JWT đầy đủ thông tin
            var dbUser = await _userRepository.GetByEmailAsync(user.Email);

            // 7. Tạo JWT Token
            var token = _tokenService.GenerateJwtToken(dbUser!);

            return new AuthResponse
            {
                Token = token,
                Username = dbUser!.Username,
                Email = dbUser.Email,
                RoleName = dbUser.Role.RoleName
            };
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null)
            {
                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }

            // Kiểm tra mật khẩu băm
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            if (!isPasswordValid)
            {
                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }

            var token = _tokenService.GenerateJwtToken(user);

            return new AuthResponse
            {
                Token = token,
                Username = user.Username,
                Email = user.Email,
                RoleName = user.Role.RoleName
            };
        }

        public async Task<AuthResponse> ContinueWithGoogleAsync(GoogleLoginRequest request)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
                // Xác thực token với Google ClientId
                var googleClientId = _config["Google:ClientId"];
                payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { googleClientId }
                });
            }
            catch (Exception)
            {
                throw new BadHttpRequestException("Google ID Token không hợp lệ!");
            }

            // Tìm User theo Email Google cung cấp
            var user = await _userRepository.GetByEmailAsync(payload.Email);

            if (user == null)
            {
                // Nếu chưa có, tự động tạo mới tài khoản
                var defaultRole = await _userRepository.GetRoleByNameAsync("Member");
                if (defaultRole == null)
                {
                    throw new Exception("Hệ thống chưa thiết lập Role 'Member' mặc định.");
                }

                user = new User
                {
                    Email = payload.Email,
                    Username = payload.Name ?? payload.Email.Split('@')[0],
                    PasswordHash = "", // Không cần password cho tài khoản Google
                    RoleId = defaultRole.RoleId,
                    IsDeleted = false
                };

                await _userRepository.AddAsync(user);
                await _userRepository.SaveChangesAsync();

                // Tải lại để có đầy đủ thông tin Role
                user = await _userRepository.GetByEmailAsync(user.Email);
            }

            var token = _tokenService.GenerateJwtToken(user!);

            return new AuthResponse
            {
                Token = token,
                Username = user!.Username,
                Email = user.Email,
                RoleName = user.Role.RoleName
            };
        }
    }
}
