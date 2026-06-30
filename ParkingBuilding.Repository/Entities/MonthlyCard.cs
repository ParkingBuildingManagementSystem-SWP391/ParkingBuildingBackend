using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MonthlyCard
{
    public int MonthlyCardId { get; set; }

    public int UserId { get; set; }

    public int TicketId { get; set; }

    public int TariffId { get; set; }

    public int DurationMonths { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public string Status { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public virtual MonthlyTariff Tariff { get; set; } = null!;

    public virtual Ticket Ticket { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
