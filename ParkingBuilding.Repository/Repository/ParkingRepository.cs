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

        public async Task<bool> HasActiveReservationAsync(int userId)
        {
            return await _context.ParkingSessions
                .AnyAsync(s => s.UserId == userId && s.SessionStatus.Trim() == ParkingStatuses.SessionReserved && !s.IsDeleted);
        }

        public async Task<ParkingSlot?> GetSlotByIdAsync(int slotId)
        {
            return await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId);
        }

        public async Task CreateSessionAsync(ParkingSession session, ParkingSlot slot)
        {
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
                await transaction.RollbackAsync();
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
        public async Task UpdateSessionAndSlotAsync(ParkingSession session, ParkingSlot slot)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
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

        public async Task<ParkingSlot?> GetAvailableSlotForWalkInAsync(int vehicleTypeId)
        {
            return await _context.ParkingSlots
                .FromSqlInterpolated($"SELECT TOP 1 * FROM ParkingSlots WITH (UPDLOCK, ROWLOCK) WHERE SlotStatus = {ParkingStatuses.SlotAvailable} AND TypeId = {vehicleTypeId} AND IsDeleted = 0 ORDER BY SlotName ASC")
                .FirstOrDefaultAsync();
        }

        public async Task<List<ParkingSession>> GetExpiredReservationsAsync()
        {
            var expiredLimit = DateTime.UtcNow.AddMinutes(-15);
            return await _context.ParkingSessions
                .Include(ps => ps.Slot)
                .Include(ps => ps.Ticket)
                .Where(ps => ps.SessionStatus == ParkingStatuses.SessionReserved && ps.BookingTime < expiredLimit && !ps.IsDeleted)
                .ToListAsync();
        }


        public async Task<ParkingSession?> GetActiveSessionByTicketCodeAsync(string ticketCode)
        {
            return await _context.ParkingSessions
                .Include(s => s.Slot)
                .Include(s => s.Ticket)
                .Include(s => s.Type)
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
                .FirstOrDefaultAsync(s => s.SessionId == sessionId
                                     && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                                     && !s.IsDeleted);
        }

        public async Task CompleteParkingSessionAsync(ParkingSession session, ParkingSlot slot, Invoice invoice)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                };

                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                }

                session.SessionStatus = ParkingStatuses.SessionCompleted;

                await _context.Invoices.AddAsync(invoice);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }


        public async Task<List<ParkingSlot>> GetSlotsByFloorIdAsync(int floorId)
        {
            return await _context.ParkingSlots
                .Where(s => s.FloorId == floorId && !s.IsDeleted)
                .ToListAsync();
        }
    }
}