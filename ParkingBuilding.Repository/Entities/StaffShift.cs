using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class StaffShift
{
    public int ShiftId { get; set; }

    public int StaffId { get; set; }

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public decimal SystemCash { get; set; }

    public decimal? ActualCash { get; set; }

    public decimal? Difference { get; set; }

    public int TotalTransactions { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public virtual User Staff { get; set; } = null!;

    public virtual ICollection<StaffActivityLog> StaffActivityLogs { get; set; } = new List<StaffActivityLog>();
}
