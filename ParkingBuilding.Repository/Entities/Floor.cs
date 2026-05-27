using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Floor
{
    public int FloorId { get; set; }

    public string FloorName { get; set; } = null!;

    public int Capacity { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<ParkingSlot> ParkingSlots { get; set; } = new List<ParkingSlot>();
}
