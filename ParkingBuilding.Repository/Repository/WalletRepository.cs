using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class WalletRepository : IWalletRepository
    {
        private readonly ParkingManagementDbContext _context;

        public WalletRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task<Wallet?> GetWalletByUserIdAsync(int userId)
        {
            return await _context.Wallets
                .Include(w => w.WalletTransactions)
                .FirstOrDefaultAsync(w => w.UserId == userId);
        }

        public async Task<Wallet?> GetWalletByIdAsync(int walletId)
        {
            return await _context.Wallets.FindAsync(walletId);
        }

        public async Task CreateWalletAsync(Wallet wallet)
        {
            await _context.Wallets.AddAsync(wallet);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateWalletAsync(Wallet wallet)
        {
            _context.Wallets.Update(wallet);
            await _context.SaveChangesAsync();
        }

        public async Task AddTransactionAsync(WalletTransaction transaction)
        {
            await _context.WalletTransactions.AddAsync(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task<WalletTransaction?> GetTransactionByCodeAsync(string transactionCode)
        {
            return await _context.WalletTransactions
                .Include(wt => wt.Wallet)
                .FirstOrDefaultAsync(wt => wt.TransactionCode == transactionCode);
        }

        public async Task UpdateTransactionAsync(WalletTransaction transaction)
        {
            _context.WalletTransactions.Update(transaction);
            await _context.SaveChangesAsync();
        }

        public async Task<List<WalletTransaction>> GetTransactionHistoryAsync(int walletId)
        {
            return await _context.WalletTransactions
                .Where(wt => wt.WalletId == walletId)
                .OrderByDescending(wt => wt.CreatedAt)
                .ToListAsync();
        }
    }
}
