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
    public class SessionRepository : GenericRepository<ParkingSession>, ISessionRepository
    {
        public SessionRepository(ParkingManagementDbContext context) : base(context)
        {
        }

        public override async Task<ParkingSession?> GetByIdAsync(object id)
        {
            if (id is int sessionId)
            {
                return await _context.ParkingSessions
                    .Include(s => s.Slot)
                    .Include(s => s.Ticket)
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId);
            }
            else if (id is long longSessionId)
            {
                return await _context.ParkingSessions
                    .Include(s => s.Slot)
                    .Include(s => s.Ticket)
                    .FirstOrDefaultAsync(s => s.SessionId == (int)longSessionId);
            }
            return await base.GetByIdAsync(id);
        }
    }
}
