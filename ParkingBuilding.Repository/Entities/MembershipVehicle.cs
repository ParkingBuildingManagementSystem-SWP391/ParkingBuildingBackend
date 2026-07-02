using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MembershipVehicle
{
    public int MembershipVehicleId { get; set; }

    public int MembershipCardId { get; set; }

    public string LicenseVehicle { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual MembershipCard MembershipCard { get; set; } = null!;
}
