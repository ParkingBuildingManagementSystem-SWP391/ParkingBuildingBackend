using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class ManagerRepository : IManagerRepository
    {
        private readonly ParkingManagementDbContext _context;

        public ManagerRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<int> GetTotalSlotsCountAsync()
        {
            return await _context.ParkingSlots.CountAsync(s => !s.IsDeleted);
        }

        public async Task<int> GetOccupiedSlotsCountAsync()
        {
            return await _context.ParkingSlots.CountAsync(s => s.SlotStatus == ParkingStatuses.SlotOccupied && !s.IsDeleted);
        }

        public async Task<int> GetReservedSlotsCountAsync()
        {
            return await _context.ParkingSlots.CountAsync(s => s.SlotStatus == ParkingStatuses.SlotReserved && !s.IsDeleted);
        }

        public async Task<int> GetAvailableSlotsCountAsync()
        {
            return await _context.ParkingSlots.CountAsync(s => s.SlotStatus == ParkingStatuses.SlotAvailable && !s.IsDeleted);
        }

        public async Task<List<(string TypeName, int Count)>> GetVehiclesInBuildingDetailAsync()
        {
            // Một xe đang trong bãi là xe có phiên đỗ InProgress, đã CheckIn và chưa CheckOut
            var groupData = await _context.ParkingSessions
                .Where(ps => ps.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                             && ps.CheckInTime != null
                             && ps.CheckOutTime == null
                             && !ps.IsDeleted)
                .GroupBy(ps => ps.Type.TypeName)
                .Select(g => new
                {
                    TypeName = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            return groupData.Select(x => (x.TypeName, x.Count)).ToList();
        }

        public async Task<List<(int FloorId, string FloorName, int Capacity, int OccupiedCount)>> GetFloorOccupancyDetailAsync()
        {
            var floors = await _context.Floors
                .Where(f => !f.IsDeleted)
                .Select(f => new
                {
                    f.FloorId,
                    f.FloorName,
                    f.Capacity,
                    OccupiedCount = f.ParkingSlots.Count(s => s.SlotStatus == ParkingStatuses.SlotOccupied && !s.IsDeleted)
                })
                .ToListAsync();

            return floors.Select(f => (f.FloorId, f.FloorName, f.Capacity, f.OccupiedCount)).ToList();
        }

        public async Task<decimal> GetRevenueSinceAsync(DateTime sinceUtc)
        {
            return await _context.Invoices
                .Where(i => i.PaymentStatus.Trim() == "SUCCESS" && i.PaymentTime >= sinceUtc)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;
        }

        public async Task<decimal> GetRevenueRangeAsync(DateTime startUtc, DateTime endUtc)
        {
            return await _context.Invoices
                .Where(i => i.PaymentStatus.Trim() == "SUCCESS" && i.PaymentTime >= startUtc && i.PaymentTime < endUtc)
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;
        }

        public async Task<List<(string TimeLabel, int CheckInCount, int CheckOutCount, decimal Revenue)>> GetTrafficStatsAsync(
            DateTime startDateUtc, DateTime endDateUtc, string groupBy, int? vehicleTypeId)
        {
            var query = _context.ParkingSessions
                .Where(ps => ps.CheckInTime >= startDateUtc && ps.CheckInTime <= endDateUtc && !ps.IsDeleted);

            if (vehicleTypeId.HasValue)
            {
                query = query.Where(ps => ps.TypeId == vehicleTypeId.Value);
            }

            var sessions = await query
                .Include(ps => ps.Invoice)
                .ToListAsync();

            // Múi giờ Việt Nam để hiển thị nhãn thời gian
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

            // Gom nhóm trên RAM sau khi tải dữ liệu thô (linh hoạt và tránh lỗi dịch SQL Server của EF Core khi dùng DateTime phức tạp)
            var localData = sessions.Select(ps => new
            {
                CheckInLocal = TimeZoneInfo.ConvertTimeFromUtc(ps.CheckInTime.Value, vnTimeZone),
                CheckOutLocal = ps.CheckOutTime.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(ps.CheckOutTime.Value, vnTimeZone) : (DateTime?)null,
                Revenue = (ps.Invoice != null && ps.Invoice.PaymentStatus.Trim() == "SUCCESS") ? ps.Invoice.TotalAmount : 0m
            }).ToList();

            if (groupBy.ToUpper() == "HOUR")
            {
                return localData
                    .GroupBy(x => x.CheckInLocal.Hour)
                    .Select(g => (
                        TimeLabel: $"{g.Key:D2}:00",
                        CheckInCount: g.Count(),
                        CheckOutCount: g.Count(x => x.CheckOutLocal != null),
                        Revenue: g.Sum(x => x.Revenue)
                    ))
                    .OrderBy(r => r.TimeLabel)
                    .ToList();
            }
            else // Mặc định hoặc "DAY"
            {
                return localData
                    .GroupBy(x => x.CheckInLocal.Date)
                    .Select(g => (
                        TimeLabel: g.Key.ToString("yyyy-MM-dd"),
                        CheckInCount: g.Count(),
                        CheckOutCount: g.Count(x => x.CheckOutLocal != null),
                        Revenue: g.Sum(x => x.Revenue)
                    ))
                    .OrderBy(r => r.TimeLabel)
                    .ToList();
            }
        }

        public async Task<List<ParkingSession>> GetParkingSessionsForExportAsync(DateTime startDateUtc, DateTime endDateUtc, int? vehicleTypeId)
        {
            var query = _context.ParkingSessions
                .Include(ps => ps.Slot)
                .Include(ps => ps.Type)
                .Include(ps => ps.Invoice)
                    .ThenInclude(i => i.Staff)
                .Where(ps => ps.CheckInTime >= startDateUtc && ps.CheckInTime <= endDateUtc && !ps.IsDeleted);

            if (vehicleTypeId.HasValue)
            {
                query = query.Where(ps => ps.TypeId == vehicleTypeId.Value);
            }

            return await query.OrderBy(ps => ps.CheckInTime).ToListAsync();
        }

        public async Task<ParkingSlot?> GetSlotDetailWithActiveSessionAsync(int slotId)
        {
            return await _context.ParkingSlots
                .Include(s => s.Floor)
                .Include(s => s.ParkingSessions)
                    .ThenInclude(ps => ps.Type)
                .Include(s => s.ParkingSessions)
                    .ThenInclude(ps => ps.User)
                        .ThenInclude(u => u.Role)
                .FirstOrDefaultAsync(s => s.SlotId == slotId && !s.IsDeleted);
        }

        public async Task<decimal> GetTotalRevenueAsync()
        {
            return await _context.Invoices
                .Where(i => i.PaymentStatus.Trim() == "SUCCESS")
                .SumAsync(i => (decimal?)i.TotalAmount) ?? 0m;
        }

    }
}
