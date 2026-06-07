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
    /// <summary>
    /// Repository quản lý truy cập cơ sở dữ liệu cho bảng ParkingSlots (Ô đỗ xe).
    /// </summary>
    public class SlotRepository : ISlotRepository
    {
        private readonly ParkingManagementDbContext _context;

        public SlotRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<ParkingSlot?> GetByIdAsync(int slotId)
        {
            return await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId);
        }

        public async Task UpdateAsync(ParkingSlot slot)
        {
            _context.ParkingSlots.Update(slot);
            await _context.SaveChangesAsync();
        }
    }
}
