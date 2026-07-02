using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IWalletService
    {
        Task<decimal> GetBalanceAsync(int userId);
        Task<List<WalletTransactionDto>> GetHistoryAsync(int userId);
        Task<string> CreateDepositUrlAsync(int userId, decimal amount, string ipAddress);
        Task<PaymentResultDto> ConfirmDepositPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus);
        Task<bool> ProcessWalletPaymentAsync(int userId, decimal amount, string description);
    }
}
