using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MembershipCardTransaction
{
    public int MembershipTransactionId { get; set; }

    public int MembershipCardId { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public decimal UnitPrice { get; set; }

    public string? TransactionCode { get; set; }

    public DateTime TransactionAt { get; set; }

    public string TransactionType { get; set; } = null!;

    public string TransactionStatus { get; set; } = null!;

    public virtual MembershipCard MembershipCard { get; set; } = null!;
}
