using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IParkingQueryService
    {
        Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId);
        Task<MyBookingsDashboardDto> GetMyBookingsAsync(int userId);
        Task<List<ActiveSessionResponseDto>> GetActiveSessionsAsync();
        Task<LocateVehicleResponseDto?> LocateVehicleAsync(string licensePlate);
        Task<List<ParkingSlotResponseDto>> GetSlotsAsync(int? typeId, string? status);
    }
}
