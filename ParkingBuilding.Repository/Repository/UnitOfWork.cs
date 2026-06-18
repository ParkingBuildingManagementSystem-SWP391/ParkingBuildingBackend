using Microsoft.EntityFrameworkCore.Storage;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ParkingManagementDbContext _context;
        private IDbContextTransaction? _transaction;

        public UnitOfWork(ParkingManagementDbContext context)
        {
            _context = context;
            Slots = new SlotRepository(_context);
            Sessions = new SessionRepository(_context);
            Invoices = new InvoiceRepository(_context);
            Users = new UserRepository(_context);
        }

        public ISlotRepository Slots { get; }
        public ISessionRepository Sessions { get; }
        public IInvoiceRepository Invoices { get; }
        public IUserRepository Users { get; }

        public async Task<bool> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task BeginTransactionAsync()
        {
            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitAsync()
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _context.Dispose();
        }
    }
}