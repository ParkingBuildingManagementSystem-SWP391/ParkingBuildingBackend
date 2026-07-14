using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class StaffActivityLog
{
    public long LogId { get; set; }

    public int StaffId { get; set; }

    public int? ShiftId { get; set; }

    public int? SessionId { get; set; }

    public string ActionType { get; set; } = null!;

    public string Description { get; set; } = null!;

    public DateTime Timestamp { get; set; }

    public string? LicensePlate { get; set; }

    public string? IpAddress { get; set; }

    public virtual ParkingSession? Session { get; set; }

    public virtual StaffShift? Shift { get; set; }

    public virtual User Staff { get; set; } = null!;
}
