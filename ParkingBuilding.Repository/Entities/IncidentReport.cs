using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class IncidentReport
{
    public int IncidentId { get; set; }

    public int SessionId { get; set; }

    public string IssueType { get; set; } = null!;

    public string? Description { get; set; }

    public int ReportedId { get; set; }

    public int? ResolvedId { get; set; }

    public string Status { get; set; } = null!;

    public virtual User Reported { get; set; } = null!;

    public virtual User? Resolved { get; set; }

    public virtual ParkingSession Session { get; set; } = null!;
}
