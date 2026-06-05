using ParkingBuilding.Service.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IAdminService
    {
        Task<IEnumerable<UserResponseDto>> GetAllUsersAsync();

        Task<bool> updateUserAsync(UpdateUserRequestDto request);

        Task<UserResponseDto> CreateUserAsync(CreateUserRequestDto request);
    }
}
