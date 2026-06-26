using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Invoice
{
    public int InvoiceId { get; set; }

    public int SessionId { get; set; }

    public decimal TotalAmount { get; set; }

    public DateTime? PaymentTime { get; set; }

    public int? StaffId { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string PaymentStatus { get; set; } = null!;

    public string? TransactionCode { get; set; }

    public DateTime CreatedDate { get; set; }

    public DateTime? UpdatedDate { get; set; }

    public virtual ParkingSession Session { get; set; } = null!;

    public virtual User? Staff { get; set; }
}
