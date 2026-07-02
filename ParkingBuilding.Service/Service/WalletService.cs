using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class WalletService : IWalletService
    {
        private readonly IWalletRepository _walletRepository;
        private readonly IVnPayService _vnPayService;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<WalletService> _logger;

        public WalletService(
            IWalletRepository walletRepository,
            IVnPayService vnPayService,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<WalletService> logger)
        {
            _walletRepository = walletRepository;
            _vnPayService = vnPayService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
        }

        public async Task<decimal> GetBalanceAsync(int userId)
        {
            var wallet = await GetOrCreateWalletAsync(userId);
            return wallet.Balance;
        }

        public async Task<List<WalletTransactionDto>> GetHistoryAsync(int userId)
        {
            var wallet = await GetOrCreateWalletAsync(userId);
            var txs = await _walletRepository.GetTransactionHistoryAsync(wallet.WalletId);
            return txs.Select(t => new WalletTransactionDto
            {
                TransactionId = t.TransactionId,
                Amount = t.Amount,
                TransactionType = t.TransactionType,
                Status = t.Status,
                Description = t.Description,
                CreatedAt = t.CreatedAt
            }).ToList();
        }

        public async Task<string> CreateDepositUrlAsync(int userId, decimal amount, string ipAddress)
        {
            if (amount <= 0)
                throw new ArgumentException("Số tiền nạp phải lớn hơn 0 VNĐ.");

            var wallet = await GetOrCreateWalletAsync(userId);
            string txnRef = $"WDEP_{wallet.WalletId}_{DateTime.UtcNow.Ticks}";

            // 1. Lưu giao dịch PENDING
            var tx = new WalletTransaction
            {
                WalletId = wallet.WalletId,
                Amount = amount,
                TransactionType = "DEPOSIT",
                Status = "PENDING",
                Description = $"Nạp tiền vào ví điện tử số tiền {amount:N0} VNĐ",
                TransactionCode = txnRef
            };
            await _walletRepository.AddTransactionAsync(tx);

            // 2. Gọi VNPay sinh URL
            return _vnPayService.CreatePaymentUrl(
                txnRef: txnRef,
                amount: amount,
                orderInfo: $"Nap tien vao vi tai khoan {userId}",
                returnUrl: _vnPayConfig.ReturnUrl + "?type=wallet",
                ipAddress: ipAddress
            );
        }

        public async Task<PaymentResultDto> ConfirmDepositPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            var tx = await _walletRepository.GetTransactionByCodeAsync(txnRef);
            if (tx == null)
                return new PaymentResultDto { Success = false, Message = "Giao dịch ví không tồn tại." };

            if (tx.Status != "PENDING")
                return new PaymentResultDto { Success = false, Message = "Giao dịch đã được xử lý từ trước." };

            if (responseCode == "00" && transactionStatus == "00")
            {
                tx.Status = "SUCCESS";
                await _walletRepository.UpdateTransactionAsync(tx);

                // Cộng số dư ví
                var wallet = await _walletRepository.GetWalletByIdAsync(tx.WalletId);
                if (wallet != null)
                {
                    wallet.Balance += amount;
                    wallet.UpdatedAt = DateTime.UtcNow;
                    await _walletRepository.UpdateWalletAsync(wallet);
                }

                return new PaymentResultDto { Success = true, Message = "Nạp tiền thành công." };
            }
            else
            {
                tx.Status = "FAILED";
                await _walletRepository.UpdateTransactionAsync(tx);
                return new PaymentResultDto { Success = false, Message = "Thanh toán thất bại từ VNPay." };
            }
        }

        public async Task<bool> ProcessWalletPaymentAsync(int userId, decimal amount, string description)
        {
            var wallet = await GetOrCreateWalletAsync(userId);
            if (wallet.Balance < amount)
            {
                _logger.LogWarning("Tài xế ID {UserId} không đủ số dư ví. Yêu cầu: {Req}, Có: {Have}", userId, amount, wallet.Balance);
                return false;
            }

            // Trừ tiền và lưu giao dịch SUCCESS
            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;
            await _walletRepository.UpdateWalletAsync(wallet);

            var tx = new WalletTransaction
            {
                WalletId = wallet.WalletId,
                Amount = -amount, // Âm tiền biểu thị giao dịch thanh toán
                TransactionType = "PAYMENT",
                Status = "SUCCESS",
                Description = description,
                TransactionCode = $"WPAY_{DateTime.UtcNow.Ticks}"
            };
            await _walletRepository.AddTransactionAsync(tx);
            return true;
        }

        private async Task<Wallet> GetOrCreateWalletAsync(int userId)
        {
            var wallet = await _walletRepository.GetWalletByUserIdAsync(userId);
            if (wallet == null)
            {
                wallet = new Wallet
                {
                    UserId = userId,
                    Balance = 0.00m,
                    CreatedAt = DateTime.UtcNow
                };
                await _walletRepository.CreateWalletAsync(wallet);
            }
            return wallet;
        }
    }
}
