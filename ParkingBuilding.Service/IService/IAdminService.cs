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

        // 1. API 1: Nghiệp vụ lấy toàn bộ phiên đỗ
        Task<List<ParkingSessionResponeDto>> GetAllParkingSessionsAsync();

        // 2. API 2: Nghiệp vụ lọc tìm kiếm phiên đỗ
        Task<List<ParkingSessionResponeDto>> GetParkingSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            string? username,
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate);

        // 3. API 3: Nghiệp vụ tìm chi tiết phiên đỗ qua mã vé
        Task<ParkingSessionDetailResponeDto?> GetSessionDetailByTicketCodeAsync(string ticketCode);
    }
}
