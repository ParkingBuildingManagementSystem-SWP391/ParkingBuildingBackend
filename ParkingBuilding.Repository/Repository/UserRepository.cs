using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    /// <summary>
    /// Repository quản lý truy cập cơ sở dữ liệu cho bảng Users và Roles.
    /// </summary>
    public class UserRepository : IUserRepository
    {
        private readonly ParkingManagementDbContext _context;

        public UserRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Tìm tài khoản người dùng theo email và nạp kèm thông tin Role.
        /// </summary>
        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users
                .Include(u => u.Role) 
                .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted);
        }

        public async Task AddAsync(User user)
        {
            await _context.Users.AddAsync(user);
        }

        public async Task<bool> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync() > 0;
        }

        /// <summary>
        /// Tìm đối tượng Role theo tên (ví dụ: 'Admin', 'Staff', 'Registered_Driver').
        /// </summary>
        public async Task<Role?> GetRoleByNameAsync(string roleName)
        {
            return await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == roleName && !r.IsDeleted);
        }

        public async Task<IEnumerable<User>> GetAllUsersWithRolesAsync()
        {
            return await _context.Users
                .Include(u => u.Role) 
                .Where(u => !u.IsDeleted)
                .ToListAsync();
        }

        public async Task<User?> GetByIdAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted);
        }
    }
}
