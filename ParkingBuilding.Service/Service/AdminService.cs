using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ quản trị hệ thống (Admin Management).
    /// Cung cấp quyền hạn cho Admin: Tạo tài khoản nhân viên/tài xế, cập nhật thông tin và phân quyền người dùng.
    /// </summary>
    public class AdminService : IAdminService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<AdminService> _logger;

        public AdminService(IUnitOfWork unitOfWork, ILogger<AdminService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        /// <summary>
        /// Cập nhật thông tin tài khoản người dùng hoặc thay đổi phân quyền (Role) của họ trong hệ thống.
        /// </summary>
        public async Task<bool> updateUserAsync(UpdateUserRequestDto request)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(request.UserId);
            if (user == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy tài khoản người dùng với ID: {request.UserId}");
            }
            if (!string.IsNullOrEmpty(request.RoleName))
            {
                var role = await _unitOfWork.Users.GetRoleByNameAsync(request.RoleName);
                if (role == null)
                {
                    throw new ArgumentException($"Hệ thống không tồn tại phân quyền có tên: '{request.RoleName}'. Vui lòng kiểm tra lại!");
                }
                user.RoleId = role.RoleId;
            }

            if (!string.IsNullOrEmpty(request.phoneNumber))
            {
                user.PhoneNumber = request.phoneNumber;
            }

            if (!string.IsNullOrEmpty(request.userName))
            {
                user.Username = request.userName;
            }

            if (!string.IsNullOrEmpty(request.email))
            {
                user.Email = request.email;
            }

            return await _unitOfWork.SaveChangesAsync();
        }

        /// <summary>
        /// Admin trực tiếp tạo tài khoản mới cho nhân viên (Staff) hoặc tài xế (Driver) mà không cần qua luồng OTP.
        /// </summary>
        public async Task<UserResponseDto> CreateUserAsync(CreateUserRequestDto request)
        {
            var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                throw new ArgumentException("Email này đã được sử dụng bởi tài khoản khác trong hệ thống.");
            }

            // 2. Kiểm tra Role có tồn tại hợp lệ hay không
            var role = await _unitOfWork.Users.GetRoleByNameAsync(request.RoleName);
            if (role == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy vai trò '{request.RoleName}' trong hệ thống.");
            }

            // 3. Thực hiện băm (hash) mật khẩu bằng BCrypt
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);

            // 4. Tạo thực thể User mới
            var newUser = new User
            {
                Username = request.Username,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = hashedPassword,
                RoleId = role.RoleId,
                IsDeleted = false // Mặc định isdelete = 0
            };

            // 5. Lưu vào Database
            await _unitOfWork.Users.AddAsync(newUser);
            var isSuccess = await _unitOfWork.SaveChangesAsync();
            if (!isSuccess)
            {
                throw new Exception("Lỗi hệ thống! Không thể tạo tài khoản vào lúc này.");
            }

            // 6. Trả về kết quả sau khi tạo thành công (Không trả về password hash)
            return new UserResponseDto
            {
                Id = newUser.UserId, 
                Name = newUser.Username,
                Email = newUser.Email,
                PhoneNumber = newUser.PhoneNumber,
                Role = role.RoleName
            };
        }

        /// <summary>
        /// Lấy danh sách toàn bộ tài khoản người dùng đang hoạt động trong hệ thống (Không bao gồm các tài khoản đã bị xóa IsDeleted = true).
        /// </summary>
        public async Task<IEnumerable<UserResponseDto>> GetAllUsersAsync()
        {
            var users = await _unitOfWork.Users.GetAllUsersWithRolesAsync();

            return users.Select(u => new UserResponseDto
            {
                Id = u.UserId,
                Name = u.Username,
                Email = u.Email, 
                PhoneNumber = u.PhoneNumber, 
                Role = u.Role.RoleName 
            });
        }

        // 1. Lấy toàn bộ danh sách phiên đỗ (không điều kiện)
        public async Task<List<ParkingSessionResponeDto>> GetAllParkingSessionsAsync()
        {
            _logger.LogInformation("Executing GetAllParkingSessionsAsync in Service.");
            try
            {
                var sessions = await _unitOfWork.Sessions.GetAllSessionsWithDetailsAsync();

                return sessions.Select(s => new ParkingSessionResponeDto
                {
                    SlotName = s.Slot?.SlotName ?? "Không xác định",
                    TypeId = s.TypeId,
                    TicketCode = s.Ticket?.TicketCode,
                    SessionStatus = s.SessionStatus.Trim()
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Service GetAllParkingSessionsAsync: {Message}", ex.Message);
                throw;
            }
        }

        // 2. Lấy danh sách kèm theo các bộ lọc
        // 2. Lấy danh sách kèm theo các bộ lọc
        public async Task<List<ParkingSessionResponeDto>> GetParkingSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            int? isRegistered,
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate)
        {
            _logger.LogInformation("Executing GetParkingSessionsWithFiltersAsync in Service. isRegistered filter value: {isRegistered}, fromDate={FromDate}, toDate={ToDate}", isRegistered, fromDate, toDate);
            try
            {
                // Giữ nguyên múi giờ Local (không convert sang UTC) để khớp với dữ liệu Local trong DB
                DateTime? queryFrom = fromDate;
                DateTime? queryTo = toDate;

                // Truyền queryFrom, queryTo xuống Repository tầng DB
                var sessions = await _unitOfWork.Sessions.GetSessionsWithFiltersAsync(
                    licenseVehicle, slotName, isRegistered, typeId, sessionStatus, queryFrom, queryTo);

                return sessions.Select(s => new ParkingSessionResponeDto
                {
                    SlotName = s.Slot?.SlotName ?? "Không xác định",
                    TypeId = s.TypeId,
                    TicketCode = s.Ticket?.TicketCode,
                    SessionStatus = s.SessionStatus.Trim()
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Service GetParkingSessionsWithFiltersAsync: {Message}", ex.Message);
                throw;
            }
        }


        // 3. Tra cứu chi tiết phiên đỗ qua mã vé xe
        public async Task<ParkingSessionDetailResponeDto?> GetSessionDetailByTicketCodeAsync(string ticketCode)
        {
            if (Helpers.QrCodeParserHelper.TryParseQr(ticketCode, out var parsedTicket, out var parsedPlate, out var parsedSessionId, out var parsedSlot))
            {
                ticketCode = parsedTicket!;
            }
            var session = await _unitOfWork.Sessions.GetSessionDetailByTicketCodeAsync(ticketCode);
            if (session == null)
            {
                return null; // Không tìm thấy
            }

            // Trả về DTO chứa đầy đủ thông tin chi tiết bao gồm ảnh chụp
            return new ParkingSessionDetailResponeDto
            {
                Username = session.User?.Username ?? "Khách vãng lai",
                SlotName = session.Slot?.SlotName ?? "N/A",
                LicenseVehicle = session.LicenseVehicle,
                TypeId = session.TypeId,
                BookingTime = session.BookingTime,
                CheckInTime = session.CheckInTime,
                CheckOutTime = session.CheckOutTime,
                CheckInImageUrl = session.CheckInImageUrl,
                CheckOutImageUrl = session.CheckOutImageUrl,
                SessionStatus = session.SessionStatus.Trim()
            };
        }
    }
}
