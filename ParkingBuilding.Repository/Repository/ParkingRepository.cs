using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class ParkingRepository : IParkingRepository
    {
        private readonly ParkingManagementDbContext _context;

        public ParkingRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<bool> HasActiveReservationAsync(int userId, int typeId)
        {
            return await _context.ParkingSessions
                .AnyAsync(s => s.UserId == userId && s.TypeId == typeId && s.SessionStatus.Trim() == ParkingStatuses.SessionReserved && !s.IsDeleted);
        }

        public async Task<ParkingSlot?> GetSlotByIdAsync(int slotId)
        {
            return await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId);
        }

        public async Task CreateSessionAsync(ParkingSession session, ParkingSlot slot)
        {
            // Kiểm tra xem đã có giao dịch (Transaction) nào đang hoạt động trên DbContext này chưa
            var isOuterTransaction = _context.Database.CurrentTransaction != null;

            // Chỉ bắt đầu transaction mới nếu chưa có transaction nào bên ngoài hoạt động
            var transaction = isOuterTransaction ? null : await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.ParkingSessions.AddAsync(session);
                _context.ParkingSlots.Update(slot);
                await _context.SaveChangesAsync();

                // Chỉ commit nếu transaction này do chính hàm này tạo ra
                if (transaction != null)
                {
                    await transaction.CommitAsync();
                }
            }
            catch (Exception)
            {
                // Chỉ rollback nếu transaction này do chính hàm này tạo ra
                if (transaction != null)
                {
                    await transaction.RollbackAsync();
                }
                throw;
            }
        }

        public async Task<ParkingSession?> GetReservedSessionByLicenseAsync(string licenseVehicle)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .FirstOrDefaultAsync(s => s.LicenseVehicle == licenseVehicle && s.SessionStatus.Trim() == ParkingStatuses.SessionReserved && !s.IsDeleted);
        }
        public async Task<ParkingSession?> GetReservedSessionByTicketCodeAsync(string ticketCode)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .FirstOrDefaultAsync(s => s.Ticket != null
                                       && s.Ticket.TicketCode.Trim() == ticketCode.Trim()
                                       && s.SessionStatus.Trim() == ParkingStatuses.SessionReserved
                                       && !s.IsDeleted);
        }

        public async Task<ParkingSession?> GetReservedSessionByTicketIdAsync(int ticketId)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .FirstOrDefaultAsync(s => s.TicketId == ticketId
                                       && s.SessionStatus.Trim() == ParkingStatuses.SessionReserved);
        }
        public async Task UpdateSessionAndSlotAsync(ParkingSession session, ParkingSlot? slot)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Nạp trạng thái hiện tại từ DB để kiểm tra trùng lặp trước khi lưu thay đổi
                var dbSession = await _context.ParkingSessions.AsNoTracking()
                    .FirstOrDefaultAsync(s => s.SessionId == session.SessionId);

                if (dbSession != null)
                {
                    // Cho phép cập nhật nếu trạng thái trong DB đang là Reserved (để check-in) hoặc đang là InProgress (để cập nhật checkout)
                    if (dbSession.SessionStatus.Trim() != ParkingStatuses.SessionReserved
                        && dbSession.SessionStatus.Trim() != ParkingStatuses.SessionInProgress
                        && session.SessionStatus == ParkingStatuses.SessionInProgress)
                    {
                        throw new Exception("Lượt đặt chỗ này đã bị thay đổi trạng thái trước đó (đã hủy hoặc đã vào bãi).");
                    }
                }

                _context.ParkingSessions.Update(session);

                if (slot != null)
                {
                    _context.ParkingSlots.Update(slot);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        /// <summary>
        /// Lấy thông tin Slot theo ID kèm theo Khóa dòng dữ liệu (UPDLOCK, ROWLOCK).
        /// Ngăn chặn các giao dịch đỗ xe song song sửa đổi trạng thái Slot đỗ này cho đến khi Transaction kết thúc.
        /// </summary>
        public async Task<ParkingSlot?> GetSlotByIdForBookingWithLockAsync(int slotId)
        {
            // Sử dụng UPDLOCK, ROWLOCK để khóa dòng dữ liệu của Slot được chọn cho đến khi Transaction kết thúc
            return await _context.ParkingSlots
                .FromSqlInterpolated($"SELECT * FROM ParkingSlots WITH (UPDLOCK, ROWLOCK) WHERE SlotId = {slotId}")
                .FirstOrDefaultAsync();
        }

        /// <summary>
        /// Tìm và trả về 1 Slot đỗ còn trống đầu tiên phù hợp với loại xe, đồng thời Khóa dòng dữ liệu (UPDLOCK, ROWLOCK) tránh tranh chấp.
        /// </summary>
        public async Task<ParkingSlot?> GetAvailableSlotForWalkInAsync(int vehicleTypeId)
        {
            return await _context.ParkingSlots
                .FromSqlInterpolated($"SELECT TOP 1 * FROM ParkingSlots WITH (UPDLOCK, ROWLOCK) WHERE SlotStatus = {ParkingStatuses.SlotAvailable} AND TypeId = {vehicleTypeId} AND IsDeleted = 0 ORDER BY SlotName ASC")
                .FirstOrDefaultAsync();
        }

        public async Task<List<ParkingSession>> GetExpiredReservationsAsync()
        {
            var now = DateTime.UtcNow;

            // Hạn trễ đơn không cọc hoặc cọc PENDING: quá 15 phút
            var limitShortTerm = now.AddMinutes(-15);

            // Hạn trễ đơn đã cọc Deposited: quá 2 tiếng (No-Show)
            var limitLongTerm = now.AddHours(-2);

            return await _context.ParkingSessions
                .Include(ps => ps.Slot)
                .Include(ps => ps.Ticket)
                .Include(ps => ps.Invoice)
                .Where(ps => ps.SessionStatus == ParkingStatuses.SessionReserved
                             && !ps.IsDeleted
                             && (
                                 // Trường hợp 1: Không cọc hoặc cọc PENDING quá 15 phút so với ExpectedCheckInTime
                                 ((ps.Invoice == null || ps.Invoice.PaymentStatus == "PENDING") && ps.ExpectedCheckInTime < limitShortTerm)

                                 ||

                                 // Trường hợp 2: Đã cọc Deposited nhưng quá 2 tiếng so với ExpectedCheckInTime (No-Show)
                                 (ps.Invoice != null && ps.Invoice.PaymentStatus == "Deposited" && ps.ExpectedCheckInTime < limitLongTerm)
                             ))
                .ToListAsync();
        }



        public async Task<ParkingSession?> GetActiveSessionByTicketCodeAsync(string ticketCode)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.Type)
                .Include(s => s.Invoice)
                .FirstOrDefaultAsync(s => s.Ticket != null
                                     && s.Ticket.TicketCode.Trim() == ticketCode.Trim()
                                     && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                                     && !s.IsDeleted);
        }

        public async Task<ParkingSession?> GetActiveSessionByIdAsync(int sessionId)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.Type)
                .Include(s => s.Invoice)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId
                                     && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                                     && !s.IsDeleted);
        }
        /// <summary>
        /// Tạo mới phiên đỗ vãng lai (Walk-in) sử dụng Cơ chế Khóa Database và Transaction.
        /// Tìm slot trống, đổi trạng thái slot sang Occupied, lưu Session đỗ xe và Vé mới tạo trong một giao dịch an toàn tuyệt đối.
        /// </summary>
        public async Task<ParkingSession?> CreateWalkInSessionWithLockAsync(string licenseVehicle, int vehicleTypeId, string? checkInImageUrl, Ticket ticket)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Khóa bảng và lấy 1 vị trí trống ngay trong transaction để tránh tranh chấp (concurrency)
                var slot = await _context.ParkingSlots
                    .FromSqlInterpolated($"SELECT TOP 1 * FROM ParkingSlots WITH (UPDLOCK, ROWLOCK) WHERE SlotStatus = {ParkingStatuses.SlotAvailable} AND TypeId = {vehicleTypeId} AND IsDeleted = 0 ORDER BY SlotName ASC")
                    .FirstOrDefaultAsync();

                if (slot == null) return null;

                // 2. Cập nhật trạng thái slot
                slot.SlotStatus = ParkingStatuses.SlotOccupied;
                _context.ParkingSlots.Update(slot);

                // 3. Tạo phiên đỗ mới
                var newSession = new ParkingSession
                {
                    UserId = null,
                    SlotId = slot.SlotId,
                    LicenseVehicle = licenseVehicle,
                    TypeId = vehicleTypeId,
                    CheckInTime = DateTime.UtcNow,
                    CheckInImageUrl = checkInImageUrl,
                    SessionStatus = ParkingStatuses.SessionInProgress,
                    Ticket = ticket,
                    IsDeleted = false
                };

                await _context.ParkingSessions.AddAsync(newSession);
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return newSession;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task CompleteParkingSessionAsync(ParkingSession session, ParkingSlot slot, Invoice invoice)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }

                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                }

                session.SessionStatus = ParkingStatuses.SessionCompleted;

                var sessionId = invoice.SessionId > 0 ? invoice.SessionId : (invoice.Session?.SessionId ?? session.SessionId);
                var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.SessionId == sessionId);

                if (existingInvoice != null)
                {
                    existingInvoice.TotalAmount = invoice.TotalAmount;
                    existingInvoice.PaymentMethod = invoice.PaymentMethod;
                    existingInvoice.PaymentStatus = invoice.PaymentStatus;
                    existingInvoice.PaymentTime = invoice.PaymentTime;
                    existingInvoice.StaffId = invoice.StaffId;
                    existingInvoice.UpdatedDate = DateTime.UtcNow;

                    _context.Invoices.Update(existingInvoice);
                }
                else
                {
                    await _context.Invoices.AddAsync(invoice);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<User?> GetStaffByIdAsync(int staffId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.UserId == staffId);

           
        }

        public async Task<ParkingSession?> GetActiveSessionByLicensePlateAsync(string licensePlate)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.Type)
                .Include(s => s.Invoice)
                .FirstOrDefaultAsync(s => s.LicenseVehicle.Trim().ToUpper() == licensePlate.Trim().ToUpper()
                                       && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                                       && !s.IsDeleted);
        }



        public async Task<List<ParkingSlot>> GetSlotsByFloorIdAsync(int floorId)
        {
            return await _context.ParkingSlots
                .Where(s => s.FloorId == floorId && !s.IsDeleted)
                .ToListAsync();
        }


        // Thêm method này vào class ParkingRepository
        public async Task<List<ParkingSession>> GetSessionsByUserIdAsync(int userId)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                    .ThenInclude(slot => slot.Floor)
                .Include(s => s.Ticket)
                .Include(s => s.Invoice) // Nạp hóa đơn để lấy thông tin chi tiêu
                .Where(s => s.UserId == userId && s.IsDeleted == false)
                .OrderByDescending(s => s.BookingTime)
                .ToListAsync();
        }

        public async Task<List<ParkingSession>> GetActiveSessionsAsync()
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.Type)
                .Where(s => (s.SessionStatus == ParkingStatuses.SessionReserved || s.SessionStatus == ParkingStatuses.SessionInProgress) && !s.IsDeleted)
                .ToListAsync();
        }
    }
}