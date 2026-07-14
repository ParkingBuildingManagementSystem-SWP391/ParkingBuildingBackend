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
        Task<bool> UpdateVehicleTypePricingAsync(int typeId, decimal dayRate, decimal nightRate, decimal fullDayRate, decimal monthlyPrice, decimal firstHourRate, decimal subsequentHourRate);
        Task<UpdateMembershipTierPriceResponse?> UpdateMembershipTierPricingAsync(UpdateMembershipTierPriceRequest request);
        Task<List<ManagerMembershipCardResponseDto>> GetMembershipCardsAsync(string? status, string? search);
        Task<ManagerCancelMembershipCardResultDto> CancelMembershipCardByManagerAsync(int cardId);
        Task<bool> LockParkingSlotAsync(int slotId);
        Task<bool> UnlockParkingSlotAsync(int slotId);
    }
}
