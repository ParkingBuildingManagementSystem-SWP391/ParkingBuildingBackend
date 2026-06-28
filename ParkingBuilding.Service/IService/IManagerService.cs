using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IManagerService
    {
        Task<DashboardSummaryResponse> GetDashboardSummaryAsync();
        Task<List<TrafficStatsResponse>> GetTrafficStatsticsAsync(TrafficStatsRequest request);
        Task<ReportExportResponse> ExportReportAsync(ReportExportRequest request);
        Task<SlotDetailResponse?> GetSlotDetailAsync(int slotId);
        Task<bool> UpdateVehicleTypePricingAsync(int typeId, decimal dayRate, decimal nightRate, decimal fullDayRate, int? maxHoursPerTurn, decimal monthlyPrice);
        Task<bool> CancelMonthlyCardAsync(int monthlyCardId);
        Task<List<object>> GetAllMonthlyCardsAsync();



    }
}
