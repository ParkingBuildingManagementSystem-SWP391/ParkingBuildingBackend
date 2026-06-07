using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    /// <summary>
    /// Repository quản lý truy cập cơ sở dữ liệu cho bảng ParkingSessions (Phiên đỗ xe).
    /// </summary>
    public class SessionRepository : ISessionRepository
    {
        private readonly ParkingManagementDbContext _context;

        public SessionRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<ParkingSession?> GetByIdAsync(long sessionId)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId);
        }

        public async Task UpdateAsync(ParkingSession session)
        {
            _context.ParkingSessions.Update(session);
            await _context.SaveChangesAsync();
        }
    }
}
