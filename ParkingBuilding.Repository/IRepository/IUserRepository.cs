
﻿using ParkingBuilding.Repository.Entities;
using System;

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IUserRepository : IGenericRepository<User>
    {
        Task<User?> GetByEmailAsync(string email);
        Task<Role?> GetRoleByNameAsync(string roleName);
        Task<IEnumerable<User>> GetAllUsersWithRolesAsync();
    }
}
