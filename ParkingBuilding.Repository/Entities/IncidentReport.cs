using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class IncidentReport
{
    public int IncidentId { get; set; }

    public int? SessionId { get; set; } // Thay đổi: Thêm dấu ? (int?)

    public string IssueType { get; set; } = null!;

    public string? Description { get; set; }

    public int ReportedId { get; set; }

    public int? ResolvedId { get; set; }

    public string Status { get; set; } = null!;

    // CÁC TRƯỜNG THÊM MỚI:
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ResolvedAt { get; set; }

    public string? ResolutionNotes { get; set; }

    public decimal? FineAmount { get; set; }

    public string? ImageProofUrl { get; set; }

    // Các Navigation properties
    public virtual User Reported { get; set; } = null!;

    public virtual User? Resolved { get; set; }

    public virtual ParkingSession? Session { get; set; } // Thay đổi: Thêm dấu ? (ParkingSession?)
}