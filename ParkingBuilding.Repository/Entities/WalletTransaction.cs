using System;

namespace ParkingBuilding.Repository.Entities
{
    public class WalletTransaction
    {
        public int TransactionId { get; set; }
        public int WalletId { get; set; }
        public decimal Amount { get; set; }
        public string TransactionType { get; set; } = null!; // "DEPOSIT", "PAYMENT"
        public string Status { get; set; } = "PENDING";      // "PENDING", "SUCCESS", "FAILED"
        public string? Description { get; set; }
        public string? TransactionCode { get; set; }          // Mã giao dịch VNPay nếu nạp tiền
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual Wallet Wallet { get; set; } = null!;
    }
}
