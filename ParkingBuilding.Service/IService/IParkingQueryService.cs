using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IParkingQueryService
    {
        Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId);
        Task<MyBookingsDashboardDto> GetMyBookingsAsync(int userId);
    }
}
