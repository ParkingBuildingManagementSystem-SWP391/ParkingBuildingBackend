using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ quản trị hệ thống (Admin Management).
    /// Cung cấp quyền hạn cho Admin: Tạo tài khoản nhân viên/tài xế, cập nhật thông tin và phân quyền người dùng.
    /// </summary>
    public class AdminService : IAdminService
    {
        private readonly IUnitOfWork _unitOfWork;

        public AdminService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
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
    }
}
