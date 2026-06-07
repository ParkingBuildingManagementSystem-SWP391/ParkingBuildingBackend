using Google.Apis.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;

using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ xử lý xác thực (Authentication).
    /// Chức năng: Đăng ký thành viên, xác thực mã OTP qua Email, đăng nhập truyền thống và đăng nhập Google (SSO).
    /// </summary>
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

        /// <summary>
        /// Đăng ký tài khoản mới (Bước 1):
        /// - Mã hóa mật khẩu bằng BCrypt.
        /// - Sinh mã OTP ngẫu nhiên 6 chữ số và lưu thông tin đăng ký tạm thời vào MemoryCache trong 5 phút.
        /// - Gửi email OTP xác nhận về hòm thư người dùng.
        /// </summary>
        public async Task<string> RegisterAsync(RegisterRequest request)
        {
            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                throw new BadHttpRequestException("Email này đã được sử dụng bởi tài khoản khác!");
            }

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var otp = new Random().Next(100000, 999999).ToString();

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


        /// <summary>
        /// Xác thực đăng ký (Bước 2):
        /// - So khớp mã OTP người dùng nhập với OTP lưu trong MemoryCache.
        /// - Nếu khớp, tạo mới User chính thức trong CSDL với Role mặc định là 'Registered_Driver' và sinh Token JWT đăng nhập.
        /// </summary>
        public async Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request)
        {
            if (!_cache.TryGetValue(request.Email, out RegisterTempData? tempData) || tempData == null)
            {
                throw new BadHttpRequestException("Yêu cầu đăng ký đã hết hạn hoặc không tồn tại. Vui lòng đăng ký lại!");
            }

            if (tempData.OtpCode != request.OtpCode)
            {
                throw new BadHttpRequestException("Mã OTP không chính xác. Vui lòng thử lại!");
            }

            // 3. Lấy Role mặc định
            var defaultRole = await _userRepository.GetRoleByNameAsync("Registered_Driver");
            if (defaultRole == null)
            {
                throw new Exception("Hệ thống chưa thiết lập Role này.");
            }

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

            _cache.Remove(request.Email);

            var dbUser = await _userRepository.GetByEmailAsync(user.Email);

            var token = _tokenService.GenerateJwtToken(dbUser!);

            return new AuthResponse
            {
                Token = token,
                Username = dbUser!.Username,
                Email = dbUser.Email,
                RoleName = dbUser.Role.RoleName
            };
        }

        /// <summary>
        /// Đăng nhập tài khoản bằng Email và Password.
        /// - Sử dụng thư viện BCrypt để đối khớp mật khẩu băm dưới CSDL.
        /// - Trả về JWT Token chứa các thông tin định danh (UserId, Role) của người dùng.
        /// </summary>
        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null)
            {
                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }

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

        /// <summary>
        /// Đăng nhập liên kết bằng tài khoản Google (Single Sign-On).
        /// - Xác thực Google ID Token gửi từ Front-end.
        /// - Nếu email này chưa tồn tại trong hệ thống, tự động tạo mới tài khoản với mật khẩu rỗng và phân quyền 'Registered_Driver'.
        /// </summary>
        public async Task<AuthResponse> ContinueWithGoogleAsync(GoogleLoginRequest request)
        {
            GoogleJsonWebSignature.Payload payload;
            try
            {
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

            var user = await _userRepository.GetByEmailAsync(payload.Email);

            if (user == null)
            {
                var defaultRole = await _userRepository.GetRoleByNameAsync("Registered_Driver");
                if (defaultRole == null)
                {
                    throw new Exception("Hệ thống chưa thiết lập Role 'Member' mặc định.");
                }

                user = new User
                {
                    Email = payload.Email,
                    Username = payload.Name ?? payload.Email.Split('@')[0],
                    PasswordHash = "", 
                    RoleId = defaultRole.RoleId,
                    IsDeleted = false
                };

                await _userRepository.AddAsync(user);
                await _userRepository.SaveChangesAsync();

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
