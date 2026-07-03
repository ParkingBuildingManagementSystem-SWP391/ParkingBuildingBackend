using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MembershipSlot
{
    public int MembershipSlotId { get; set; }

    public int MembershipCardId { get; set; }

    public int SlotId { get; set; }

    public virtual MembershipCard MembershipCard { get; set; } = null!;

    public virtual ParkingSlot Slot { get; set; } = null!;
}
