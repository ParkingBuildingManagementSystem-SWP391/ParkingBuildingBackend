using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MembershipTier
{
    public int TierId { get; set; }

    public string TierName { get; set; } = null!;

    public int DurationMonths { get; set; }

    public int MaxVehicles { get; set; }

    public int TypeId { get; set; }

    public decimal Price { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<MembershipCard> MembershipCards { get; set; } = new List<MembershipCard>();

    public virtual VehiclesType Type { get; set; } = null!;
}
