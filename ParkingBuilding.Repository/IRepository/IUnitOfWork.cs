using ParkingBuilding.Repository.Entities;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IUnitOfWork : IDisposable
    {
        ISlotRepository Slots { get; }
        ISessionRepository Sessions { get; }
        IInvoiceRepository Invoices { get; }
        IUserRepository Users { get; }

        Task<bool> SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitAsync();
        Task RollbackAsync();
    }
}