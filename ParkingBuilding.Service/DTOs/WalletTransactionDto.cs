using System;

namespace ParkingBuilding.Service.DTOs
{
    public class WalletTransactionDto
    {
        public int TransactionId { get; set; }
        public decimal Amount { get; set; }
        public string TransactionType { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
