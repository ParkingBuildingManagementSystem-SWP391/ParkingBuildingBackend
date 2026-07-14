using ParkingBuilding.Service.DTOs;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IStaffLogService
    {
        Task<StartShiftResponse> StartShiftAsync(int staffId);
        Task<EndShiftResponse> EndShiftAsync(int staffId, EndShiftRequest request);
        Task LogActivityAsync(int staffId, string actionType, string description, string? licensePlate = null, int? sessionId = null, string? ipAddress = null);
        Task<StaffShiftDto?> GetActiveShiftAsync(int staffId);
        Task UpdateShiftRevenueAsync(int shiftId, decimal cashAmount);
        Task<List<StaffShiftDto>> GetShiftsForManagerAsync(DateTime? startDate, DateTime? endDate, string? status);
        Task<List<StaffActivityLogDto>> GetActivityLogsForManagerAsync(int? staffId, string? actionType, DateTime? startDate, DateTime? endDate);
    }
}
