using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MembershipCard
{
    public int MembershipCardId { get; set; }

    public int UserId { get; set; }

    public int TierId { get; set; }

    public int TicketId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime EndTime { get; set; }

    public string Status { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public int SlotId { get; set; }

    public virtual ICollection<MembershipVehicle> MembershipVehicles { get; set; } = new List<MembershipVehicle>();

    public virtual ICollection<MembershipSlot> MembershipSlots { get; set; } = new List<MembershipSlot>();

    public virtual ParkingSlot Slot { get; set; } = null!;

    public virtual Ticket Ticket { get; set; } = null!;

    public virtual MembershipTier Tier { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
