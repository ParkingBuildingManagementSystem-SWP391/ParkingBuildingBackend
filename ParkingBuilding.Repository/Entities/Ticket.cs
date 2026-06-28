using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Ticket
{
    public int TicketId { get; set; }

    public string TicketCode { get; set; } = null!;

    public string TicketStatus { get; set; } = null!;

    public virtual ICollection<MonthlyCard> MonthlyCards { get; set; } = new List<MonthlyCard>();

    public virtual ParkingSession? ParkingSession { get; set; }
}
