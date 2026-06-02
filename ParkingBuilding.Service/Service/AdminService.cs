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
