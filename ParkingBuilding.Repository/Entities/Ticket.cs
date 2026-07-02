using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Ticket
{
    public int TicketId { get; set; }

    public string TicketCode { get; set; } = null!;

    public string TicketStatus { get; set; } = null!;

    public virtual MembershipCard? MembershipCard { get; set; }

    public virtual ParkingSession? ParkingSession { get; set; }
}
