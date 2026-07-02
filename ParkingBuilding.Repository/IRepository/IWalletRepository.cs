using ParkingBuilding.Repository.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IWalletRepository
    {
        Task<Wallet?> GetWalletByUserIdAsync(int userId);
        Task<Wallet?> GetWalletByIdAsync(int walletId);
        Task CreateWalletAsync(Wallet wallet);
        Task UpdateWalletAsync(Wallet wallet);
        Task AddTransactionAsync(WalletTransaction transaction);
        Task<WalletTransaction?> GetTransactionByCodeAsync(string transactionCode);
        Task UpdateTransactionAsync(WalletTransaction transaction);
        Task<List<WalletTransaction>> GetTransactionHistoryAsync(int walletId);
    }
}
