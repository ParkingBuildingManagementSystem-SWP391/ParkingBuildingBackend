using Google.Apis.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ xử lý xác thực (Authentication).
    /// Chức năng: Đăng ký thành viên, xác thực mã OTP qua Email, đăng nhập truyền thống và đăng nhập Google (SSO).
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ITokenService _tokenService;
        private readonly IEmailService _emailService;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _config;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUnitOfWork unitOfWork,
            ITokenService tokenService,
            IEmailService emailService,
            IMemoryCache cache,
            IConfiguration config,
            ILogger<AuthService> logger)
        {
            _unitOfWork = unitOfWork;
            _tokenService = tokenService;
            _emailService = emailService;
            _cache = cache;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Đăng ký tài khoản mới (Bước 1):
        /// - Mã hóa mật khẩu bằng BCrypt.
        /// - Sinh mã OTP ngẫu nhiên 6 chữ số và lưu thông tin đăng ký tạm thời vào MemoryCache trong 5 phút.
        /// - Gửi email OTP xác nhận về hòm thư người dùng.
        /// </summary>
        public async Task<string> RegisterAsync(RegisterRequest request)
        {
            var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
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
            var defaultRole = await _unitOfWork.Users.GetRoleByNameAsync("Registered_Driver");
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

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            _cache.Remove(request.Email);

            var dbUser = await _unitOfWork.Users.GetByEmailAsync(user.Email);

            var token = _tokenService.GenerateJwtToken(dbUser!);

            return new AuthResponse
            {
                Token = token,
                Username = dbUser!.Username,
                Email = dbUser.Email,
                RoleName = dbUser.Role.RoleName,
                PhoneNumber = dbUser.PhoneNumber ?? "Chưa có số điện thoại"
            };
        }

        /// <summary>
        /// Đăng nhập tài khoản bằng Email và Password.
        /// - Sử dụng thư viện BCrypt để đối khớp mật khẩu băm dưới CSDL.
        /// - Trả về JWT Token chứa các thông tin định danh (UserId, Role) của người dùng.
        /// </summary>
        public async Task<AuthResponse> LoginAsync(LoginRequest request, string ipAddress)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (user == null)
            {
                // GHI LOG WARNING: Không tồn tại email này trong hệ thống
                _logger.LogWarning("Đăng nhập thất bại: Email '{Email}' không tồn tại. Yêu cầu đến từ IP: {IP}.", request.Email, ipAddress);

                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }
            if (string.IsNullOrEmpty(user.PasswordHash))
            {
                _logger.LogWarning("Đăng nhập thất bại: Tài khoản '{Email}' đăng ký qua Google (mật khẩu rỗng) cố gắng đăng nhập truyền thống. Yêu cầu đến từ IP: {IP}.", request.Email, ipAddress);
                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }

            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            if (!isPasswordValid)
            {
                // GHI LOG WARNING: Nhập sai mật khẩu
                _logger.LogWarning("Đăng nhập thất bại: Nhập sai mật khẩu cho tài khoản '{Email}'. Yêu cầu đến từ IP: {IP}.", request.Email, ipAddress);

                throw new BadHttpRequestException("Email hoặc mật khẩu không chính xác!");
            }

            var token = _tokenService.GenerateJwtToken(user);

            // GHI LOG INFORMATION: Đăng nhập thành công
            _logger.LogInformation("Đăng nhập thành công: Người dùng '{Email}' (Vai trò: {Role}) đã đăng nhập thành công từ IP {IP}.",
                user.Email, user.Role.RoleName, ipAddress);

            return new AuthResponse
            {
                Token = token,
                Username = user.Username,
                Email = user.Email,
                RoleName = user.Role.RoleName,
                PhoneNumber = user.PhoneNumber ?? "Chưa có số điện thoại"
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

            var user = await _unitOfWork.Users.GetByEmailAsync(payload.Email);

            if (user == null)
            {
                var defaultRole = await _unitOfWork.Users.GetRoleByNameAsync("Registered_Driver");
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

                await _unitOfWork.Users.AddAsync(user);
                await _unitOfWork.SaveChangesAsync();

                user = await _unitOfWork.Users.GetByEmailAsync(user.Email);
            }

            var token = _tokenService.GenerateJwtToken(user!);

            return new AuthResponse
            {
                Token = token,
                Username = user!.Username,
                Email = user.Email,
                RoleName = user.Role.RoleName,
                PhoneNumber = user.PhoneNumber ?? "Chưa có số điện thoại"
            };
        }

        /// <summary>
        /// Yêu cầu đặt lại mật khẩu:
        /// - Phát sinh OTP 6 số.
        /// - Lưu vào MemoryCache trong 5 phút.
        /// - Gửi mã OTP qua Email.
        /// </summary>
        public async Task ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (user == null)
            {
                throw new BadHttpRequestException("Không tìm thấy tài khoản với email này trong hệ thống.");
            }

            // Sinh OTP ngẫu nhiên
            var otp = new Random().Next(100000, 999999).ToString();

            var cacheKey = $"ResetOtp_{request.Email}";
            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(5));

            _cache.Set(cacheKey, otp, cacheOptions);

            var mailSubject = "Mã xác thực yêu cầu khôi phục mật khẩu";
            var mailBody = $@"
                <h3>Yêu cầu khôi phục mật khẩu tài khoản!</h3>
                <p>Mã OTP để bạn đặt lại mật khẩu mới là:</p>
                <h2 style='color: #d9534f; letter-spacing: 2px;'>{otp}</h2>
                <p>Mã này có hiệu lực trong vòng 5 phút. Nếu không phải bạn yêu cầu, vui lòng bỏ qua email này.</p>
            ";

            await _emailService.SendEmailAsync(request.Email, mailSubject, mailBody);
        }

        /// <summary>
        /// Xác thực OTP và đặt mật khẩu mới.
        /// </summary>
        public async Task ResetPasswordAsync(ResetPasswordRequest request)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (user == null)
            {
                throw new BadHttpRequestException("Yêu cầu không hợp lệ.");
            }

            var cacheKey = $"ResetOtp_{request.Email}";
            if (!_cache.TryGetValue(cacheKey, out string? cachedOtp) || cachedOtp != request.OtpCode)
            {
                throw new BadHttpRequestException("Mã OTP không chính xác hoặc đã hết hạn.");
            }

            // Mã hóa mật khẩu mới và lưu vào CSDL
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            await _unitOfWork.SaveChangesAsync();

            // Xóa OTP khỏi cache
            _cache.Remove(cacheKey);
        }

        /// <summary>
        /// Người dùng tự cập nhật thông tin cá nhân (Họ tên, Email, Số điện thoại và Mật khẩu).
        /// </summary>
        public async Task UpdateProfileAsync(int userId, UpdateProfileRequest request)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new KeyNotFoundException("Không tìm thấy tài khoản người dùng.");
            }

            // Cập nhật Họ tên (lưu vào cột Username trong DB)
            user.Username = request.Username;

            // Cập nhật Số điện thoại
            if (!string.IsNullOrEmpty(request.PhoneNumber))
            {
                user.PhoneNumber = request.PhoneNumber;
            }

            // Cập nhật Email
            if (!string.IsNullOrEmpty(request.Email))
            {
                var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
                if (existingUser != null && existingUser.UserId != userId)
                {
                    throw new BadHttpRequestException("Địa chỉ Email này đã được sử dụng bởi tài khoản khác trong hệ thống.");
                }
                user.Email = request.Email;
            }

            // Nếu người dùng muốn đổi mật khẩu
            if (!string.IsNullOrEmpty(request.OldPassword) && !string.IsNullOrEmpty(request.NewPassword))
            {
                // Kiểm tra mật khẩu cũ
                if (string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
                {
                    throw new BadHttpRequestException("Mật khẩu cũ không chính xác.");
                }

                if (request.NewPassword.Length < 6)
                {
                    throw new BadHttpRequestException("Mật khẩu mới phải từ 6 ký tự trở lên.");
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
            }

            await _unitOfWork.SaveChangesAsync();
        }
    }
}
