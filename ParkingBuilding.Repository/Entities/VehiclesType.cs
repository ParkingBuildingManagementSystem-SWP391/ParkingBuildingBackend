using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class VehiclesType
{
    public int TypeId { get; set; }

    public string TypeName { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public decimal DayRate { get; set; }

    public decimal NightRate { get; set; }

    public decimal FullDayRate { get; set; }

    public decimal FirstHourRate { get; set; }

    public decimal SubsequentHourRate { get; set; }

    public virtual ICollection<MonthlyTariff> MonthlyTariffs { get; set; } = new List<MonthlyTariff>();

    public virtual ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();

    public virtual ICollection<ParkingSlot> ParkingSlots { get; set; } = new List<ParkingSlot>();
}
