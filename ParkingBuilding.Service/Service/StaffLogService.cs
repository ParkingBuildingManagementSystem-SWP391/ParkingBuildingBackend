using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class StaffLogService : IStaffLogService
    {
        private readonly ParkingManagementDbContext _context;

        public StaffLogService(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<StartShiftResponse> StartShiftAsync(int staffId)
        {
            // Kiểm tra xem nhân viên đã có ca trực nào đang hoạt động chưa
            var existingActiveShift = await _context.StaffShifts
                .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Status == "Active");

            if (existingActiveShift != null)
            {
                throw new InvalidOperationException("Bạn hiện đang có ca trực chưa đóng. Vui lòng đóng ca trực hiện tại trước.");
            }

            var shift = new StaffShift
            {
                StaffId = staffId,
                StartTime = DateTime.UtcNow,
                SystemCash = 0,
                TotalTransactions = 0,
                Status = "Active"
            };

            await _context.StaffShifts.AddAsync(shift);
            await _context.SaveChangesAsync();

            // Ghi nhận nhật ký bắt đầu ca
            await LogActivityAsync(staffId, "START_SHIFT", $"Bắt đầu ca trực mới. Mã ca: {shift.ShiftId}");

            return new StartShiftResponse
            {
                ShiftId = shift.ShiftId,
                StaffId = shift.StaffId,
                StartTime = shift.StartTime,
                Status = shift.Status
            };
        }

        public async Task<EndShiftResponse> EndShiftAsync(int staffId, EndShiftRequest request)
        {
            var shift = await _context.StaffShifts
                .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Status == "Active");

            if (shift == null)
            {
                throw new InvalidOperationException("Không tìm thấy ca trực đang hoạt động nào của bạn.");
            }

            shift.EndTime = DateTime.UtcNow;
            shift.ActualCash = request.ActualCash;
            shift.Difference = request.ActualCash - shift.SystemCash;
            shift.Status = "Closed";
            shift.Notes = request.Notes;

            _context.StaffShifts.Update(shift);
            await _context.SaveChangesAsync();

            // Ghi nhận nhật ký đóng ca
            await LogActivityAsync(staffId, "END_SHIFT", $"Kết thúc ca trực. Mã ca: {shift.ShiftId}. Tiền hệ thống: {shift.SystemCash:N0} đ, Tiền thực tế nộp: {shift.ActualCash.Value:N0} đ, Chênh lệch: {shift.Difference.Value:N0} đ.");

            return new EndShiftResponse
            {
                ShiftId = shift.ShiftId,
                EndTime = shift.EndTime.Value,
                SystemCash = shift.SystemCash,
                ActualCash = shift.ActualCash.Value,
                Difference = shift.Difference.Value,
                TotalTransactions = shift.TotalTransactions,
                Status = shift.Status
            };
        }

        public async Task LogActivityAsync(int staffId, string actionType, string description, string? licensePlate = null, int? sessionId = null, string? ipAddress = null)
        {
            // Tìm ca trực đang hoạt động của nhân viên để gán vào nhật ký (nếu có)
            var activeShift = await _context.StaffShifts
                .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Status == "Active");

            var log = new StaffActivityLog
            {
                StaffId = staffId,
                ShiftId = activeShift?.ShiftId,
                ActionType = actionType,
                Description = description,
                LicensePlate = licensePlate,
                SessionId = sessionId,
                IpAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            await _context.StaffActivityLogs.AddAsync(log);
            await _context.SaveChangesAsync();
        }

        public async Task<StaffShiftDto?> GetActiveShiftAsync(int staffId)
        {
            var shift = await _context.StaffShifts
                .Include(s => s.Staff)
                .FirstOrDefaultAsync(s => s.StaffId == staffId && s.Status == "Active");

            if (shift == null) return null;

            return new StaffShiftDto
            {
                ShiftId = shift.ShiftId,
                StaffId = shift.StaffId,
                StaffUsername = shift.Staff?.Username ?? "N/A",
                StartTime = shift.StartTime,
                EndTime = shift.EndTime,
                SystemCash = shift.SystemCash,
                ActualCash = shift.ActualCash,
                Difference = shift.Difference,
                TotalTransactions = shift.TotalTransactions,
                Status = shift.Status,
                Notes = shift.Notes
            };
        }

        public async Task UpdateShiftRevenueAsync(int shiftId, decimal cashAmount)
        {
            var shift = await _context.StaffShifts.FindAsync(shiftId);
            if (shift != null && shift.Status == "Active")
            {
                shift.SystemCash += cashAmount;
                shift.TotalTransactions += 1;
                _context.StaffShifts.Update(shift);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<List<StaffShiftDto>> GetShiftsForManagerAsync(DateTime? startDate, DateTime? endDate, string? status)
        {
            var query = _context.StaffShifts
                .Include(s => s.Staff)
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(s => s.StartTime >= startDate.Value);
            }
            if (endDate.HasValue)
            {
                query = query.Where(s => s.StartTime <= endDate.Value);
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(s => s.Status == status.Trim());
            }

            var shifts = await query
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();

            return shifts.Select(s => new StaffShiftDto
            {
                ShiftId = s.ShiftId,
                StaffId = s.StaffId,
                StaffUsername = s.Staff?.Username ?? "N/A",
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                SystemCash = s.SystemCash,
                ActualCash = s.ActualCash,
                Difference = s.Difference,
                TotalTransactions = s.TotalTransactions,
                Status = s.Status,
                Notes = s.Notes
            }).ToList();
        }

        public async Task<List<StaffActivityLogDto>> GetActivityLogsForManagerAsync(int? staffId, string? actionType, DateTime? startDate, DateTime? endDate)
        {
            var query = _context.StaffActivityLogs
                .Include(l => l.Staff)
                .AsQueryable();

            if (staffId.HasValue)
            {
                query = query.Where(l => l.StaffId == staffId.Value);
            }
            if (!string.IsNullOrWhiteSpace(actionType))
            {
                query = query.Where(l => l.ActionType == actionType.Trim());
            }
            if (startDate.HasValue)
            {
                query = query.Where(l => l.Timestamp >= startDate.Value);
            }
            if (endDate.HasValue)
            {
                query = query.Where(l => l.Timestamp <= endDate.Value);
            }

            var logs = await query
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            return logs.Select(l => new StaffActivityLogDto
            {
                LogId = l.LogId,
                StaffId = l.StaffId,
                StaffUsername = l.Staff?.Username ?? "N/A",
                ShiftId = l.ShiftId,
                ActionType = l.ActionType,
                Timestamp = l.Timestamp,
                LicensePlate = l.LicensePlate,
                SessionId = l.SessionId,
                Description = l.Description,
                IpAddress = l.IpAddress
            }).ToList();
        }
    }
}
