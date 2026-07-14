using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class ParkingSlot
{
    public int SlotId { get; set; }

    public string SlotName { get; set; } = null!;

    public int FloorId { get; set; }

    public int TypeId { get; set; }

    public string SlotStatus { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public virtual Floor Floor { get; set; } = null!;

    public virtual ICollection<MembershipCard> MembershipCards { get; set; } = new List<MembershipCard>();

    public virtual ICollection<MembershipSlot> MembershipSlots { get; set; } = new List<MembershipSlot>();

    public virtual ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();

    public virtual VehiclesType Type { get; set; } = null!;
}
