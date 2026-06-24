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

        // 1. API 1: Truy vấn danh sách không điều kiện (Đã sửa để lấy từ dưới lên)
        public async Task<List<ParkingSession>> GetAllSessionsWithDetailsAsync()
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)     // Kèm dữ liệu ô đỗ xe để lấy SlotName
                .Include(s => s.Ticket)   // Kèm dữ liệu vé để lấy TicketCode
                .Where(s => !s.IsDeleted) // Chỉ lấy các phiên đỗ chưa bị xóa logic
                .OrderByDescending(s => s.SessionId) // Xếp giảm dần theo SessionId (lấy từ dưới lên trên, phiên mới nhất lên đầu)
                .ToListAsync();
        }

        // 2. API 2: Truy vấn danh sách có nhiều bộ lọc tùy chọn (Đã sửa theo yêu cầu)
        public async Task<List<ParkingSession>> GetSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            int? isRegistered, // Thay username bằng isRegistered (1: đã đăng ký, 0: vãng lai)
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate)
        {
            var query = _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.User)
                .Where(s => !s.IsDeleted)
                .AsQueryable();

            // 1. Lọc theo biển số xe
            if (!string.IsNullOrWhiteSpace(licenseVehicle))
            {
                query = query.Where(s => s.LicenseVehicle.Contains(licenseVehicle));
            }

            // 2. Lọc theo tên ô đỗ
            if (!string.IsNullOrWhiteSpace(slotName))
            {
                query = query.Where(s => s.Slot.SlotName.Contains(slotName));
            }

            // 3. LỌC LOẠI KHÁCH HÀNG (Thay thế cho Username cũ)
            // Nhập 1: khách đã đăng ký (UserId KHÁC null)
            // Nhập 0: khách vãng lai (UserId BẰNG null)
            if (isRegistered.HasValue)
            {
                if (isRegistered.Value == 1)
                {
                    query = query.Where(s => s.UserId != null);
                }
                else if (isRegistered.Value == 0)
                {
                    query = query.Where(s => s.UserId == null);
                }
            }

            // 4. Lọc theo loại phương tiện
            if (typeId.HasValue)
            {
                query = query.Where(s => s.TypeId == typeId.Value);
            }

            // 5. Lọc theo trạng thái phiên đỗ
            if (!string.IsNullOrWhiteSpace(sessionStatus))
            {
                query = query.Where(s => s.SessionStatus == sessionStatus);
            }

            // 6. LỌC THEO THỜI GIAN CHECK-IN (Chỉ dùng CheckInTime để so sánh)
            // Lấy tất cả các phiên có CheckInTime >= fromDate
            if(fromDate.HasValue && toDate.HasValue)
            {
                query = query.Where(s => s.CheckInTime != null && s.CheckInTime >= fromDate.Value && s.CheckInTime <= toDate.Value);
            }
            else if (fromDate.HasValue)
            {
                query = query.Where(s => s.CheckInTime != null && s.CheckInTime >= fromDate.Value);
            }
            // Lấy tất cả các phiên có CheckInTime <= toDate
            else if (toDate.HasValue)
            {
                query = query.Where(s => s.CheckInTime != null && s.CheckInTime <= toDate.Value);
            }

            // 7. SẮP XẾP TỪ DƯỚI LÊN TRÊN (Mới nhất lên đầu)
            query = query.OrderByDescending(s => s.SessionId);

            return await query.ToListAsync();
        }

        // 3. API 3: Truy vết chi tiết phiên đỗ theo TicketCode
        public async Task<ParkingSession?> GetSessionDetailByTicketCodeAsync(string ticketCode)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.Ticket != null && s.Ticket.TicketCode == ticketCode && !s.IsDeleted);
        }
    }
}