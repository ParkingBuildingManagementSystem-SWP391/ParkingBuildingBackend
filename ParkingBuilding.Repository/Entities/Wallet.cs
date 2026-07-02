using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities
{
    public class Wallet
    {
        public int WalletId { get; set; }
        public int UserId { get; set; }
        public decimal Balance { get; set; } = 0.00m;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual ICollection<WalletTransaction> WalletTransactions { get; set; } = new List<WalletTransaction>();
    }
}
