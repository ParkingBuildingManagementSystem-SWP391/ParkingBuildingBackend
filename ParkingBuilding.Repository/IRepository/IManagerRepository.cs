using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IManagerRepository
    {
        Task<int> GetTotalSlotsCountAsync();
        Task<int> GetOccupiedSlotsCountAsync();
        Task<int> GetReservedSlotsCountAsync();
        Task<int> GetAvailableSlotsCountAsync();

        Task<List<(string TypeName, int Count)>> GetVehiclesInBuildingDetailAsync();
        Task<List<(int FloorId, string FloorName, int Capacity, int OccupiedCount)>> GetFloorOccupancyDetailAsync();

        Task<decimal> GetRevenueSinceAsync(DateTime sinceUtc);
        Task<decimal> GetRevenueRangeAsync(DateTime startUtc, DateTime endUtc);

        Task<List<(string TimeLabel, int CheckInCount, int CheckOutCount, decimal Revenue)>> GetTrafficStatsAsync(
            DateTime startDateUtc, DateTime endDateUtc, string groupBy, int? vehicleTypeId);

        Task<List<ParkingSession>> GetParkingSessionsForExportAsync(DateTime startDateUtc, DateTime endDateUtc, int? vehicleTypeId);

        Task<ParkingSlot?> GetSlotDetailWithActiveSessionAsync(int slotId);
        Task<decimal> GetTotalRevenueAsync();

    }
}
