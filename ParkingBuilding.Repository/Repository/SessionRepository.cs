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

        // 1. API 1: Truy vấn danh sách không điều kiện
        public async Task<List<ParkingSession>> GetAllSessionsWithDetailsAsync()
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)     // Kèm dữ liệu ô đỗ xe để lấy SlotName
                .Include(s => s.Ticket)   // Kèm dữ liệu vé để lấy TicketCode
                .Where(s => !s.IsDeleted) // Chỉ lấy các phiên đỗ chưa bị xóa logic
                .ToListAsync();
        }

        // 2. API 2: Truy vấn danh sách có nhiều bộ lọc tùy chọn
        public async Task<List<ParkingSession>> GetSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            string? username,
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate)
        {
            // Bắt đầu khởi tạo Queryable để build câu lệnh SQL động
            var query = _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.User)
                .Where(s => !s.IsDeleted)
                .AsQueryable();

            // Lọc theo biển số xe (nếu người dùng có nhập)
            if (!string.IsNullOrWhiteSpace(licenseVehicle))
            {
                query = query.Where(s => s.LicenseVehicle.Contains(licenseVehicle));
            }

            // Lọc theo tên vị trí đỗ (nếu người dùng có nhập)
            if (!string.IsNullOrWhiteSpace(slotName))
            {
                query = query.Where(s => s.Slot.SlotName.Contains(slotName));
            }

            // Lọc theo Username người đặt (nếu người dùng có nhập)
            if (!string.IsNullOrWhiteSpace(username))
            {
                query = query.Where(s => s.User.Username.Contains(username));
            }

            // Lọc theo loại phương tiện (nếu người dùng có chọn)
            if (typeId.HasValue)
            {
                query = query.Where(s => s.TypeId == typeId.Value);
            }

            // Lọc theo trạng thái phiên đỗ (nếu người dùng có chọn)
            if (!string.IsNullOrWhiteSpace(sessionStatus))
            {
                query = query.Where(s => s.SessionStatus == sessionStatus);
            }

            // LỌC THEO KHOẢNG THỜI GIAN (Vào/Ra)
            // Lựa chọn A: Theo đúng yêu cầu bằng lời của bạn
            // (Nếu không nhập Từ -> Lọc Đến trở về sau; Nếu không nhập Đến -> Lọc Từ trở về trước)
            if (fromDate.HasValue && toDate.HasValue)
            {
                // Cả hai thời gian đều được truyền -> CheckInTime và CheckOutTime phải nằm trọn trong khoảng này
                query = query.Where(s => s.CheckInTime >= fromDate.Value && s.CheckOutTime <= toDate.Value);
            }
            else if (!fromDate.HasValue && toDate.HasValue)
            {
                // Không nhập 'Từ' (fromDate) -> Lấy mốc 'Đến' trở về sau (CheckInTime >= toDate)
                query = query.Where(s => s.CheckInTime >= toDate.Value);
            }
            else if (fromDate.HasValue && !toDate.HasValue)
            {
                // Không nhập 'Đến' (toDate) -> Lấy mốc 'Từ' trở về trước (CheckInTime <= fromDate)
                query = query.Where(s => s.CheckInTime <= fromDate.Value);
            }
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