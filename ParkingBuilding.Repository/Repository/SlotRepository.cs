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
    public class SlotRepository : GenericRepository<ParkingSlot>, ISlotRepository
    {
        public SlotRepository(ParkingManagementDbContext context) : base(context)
        {
        }

        public async Task<ParkingSlot?> GetByNameAsync(string name)
        {
            return await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotName == name);
        }
    }
}
