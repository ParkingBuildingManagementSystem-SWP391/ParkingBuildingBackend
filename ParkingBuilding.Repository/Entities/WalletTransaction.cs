using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class WalletTransaction
{
    public int TransactionId { get; set; }

    public int WalletId { get; set; }

    public decimal Amount { get; set; }

    public string TransactionType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Description { get; set; }

    public string? TransactionCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Wallet Wallet { get; set; } = null!;
}
