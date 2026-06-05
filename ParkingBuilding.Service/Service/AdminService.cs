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
    public class AdminService : IAdminService
    {
        public readonly IUserRepository _userRepository;

        public AdminService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }
        public async Task<bool> AssignRoleAsync(AssignRoleRequestDto request)
        {

            var user = await _userRepository.GetByIdAsync(request.UserId);
            if (user == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy tài khoản người dùng với ID: {request.UserId}");
            }

            var role = await _userRepository.GetRoleByNameAsync(request.RoleName);
            if (role == null)
            {
                throw new ArgumentException($"Hệ thống không tồn tại phân quyền có tên: '{request.RoleName}'. Vui lòng kiểm tra lại!");
            }

            user.RoleId = role.RoleId;

            return await _userRepository.SaveChangesAsync();
        }

        public async Task<UserResponseDto> CreateUserAsync(CreateUserRequestDto request)
        {
            var existingUser = await _userRepository.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                throw new ArgumentException("Email này đã được sử dụng bởi tài khoản khác trong hệ thống.");
            }

            // 2. Kiểm tra Role có tồn tại hợp lệ hay không
            var role = await _userRepository.GetRoleByNameAsync(request.RoleName);
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
            await _userRepository.AddAsync(newUser);
            var isSuccess = await _userRepository.SaveChangesAsync();
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

        public async Task<IEnumerable<UserResponseDto>> GetAllUsersAsync()
        {
            var users = await _userRepository.GetAllUsersWithRolesAsync();

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
