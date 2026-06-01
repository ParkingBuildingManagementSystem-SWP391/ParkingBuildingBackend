using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Ticket
{
    public int TicketId { get; set; }

    public string TicketCode { get; set; } = null!;

    public string TicketStatus { get; set; } = null!;

    //public virtual ParkingSession? ParkingSession { get; set; }

    public virtual ICollection<ParkingSession> ParkingSession { get; set; } = new List<ParkingSession>();
}
