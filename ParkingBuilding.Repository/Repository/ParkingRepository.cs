using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class ParkingRepository : IParkingRepository
    {
        private readonly ParkingManagementDbContext _context;

        public ParkingRepository(ParkingManagementDbContext context) { _context = context; }

        // Kiểm tra xem User này có đang giữ chỗ nào mà chưa đến đỗ không?
        public async Task<bool> HasActiveReservationAsync(int userId)
        {
            return await _context.ParkingSessions
                .AnyAsync(s => s.UserId == userId && s.SessionStatus == "InProgress" && !s.IsDeleted);
        }

        // Lấy thông tin Slot
        public async Task<ParkingSlot?> GetSlotByIdAsync(int slotId)
        {
            return await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId);
        }

        public async Task CreateSessionAsync(ParkingSession session, ParkingSlot slot)
        {
            // BẮT ĐẦU TRANSACTION: Ngăn chặn 2 người đặt cùng lúc
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.ParkingSessions.AddAsync(session);
                _context.ParkingSlots.Update(slot);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(); // Lỗi thì hoàn tác tất cả!
                throw;
            }
        }
    }
}
