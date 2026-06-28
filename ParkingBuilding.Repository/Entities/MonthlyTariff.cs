using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class MonthlyTariff
{
    public int TariffId { get; set; }

    public int TypeId { get; set; }

    public decimal MonthlyPrice { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<MonthlyCard> MonthlyCards { get; set; } = new List<MonthlyCard>();

    public virtual VehiclesType Type { get; set; } = null!;
}
