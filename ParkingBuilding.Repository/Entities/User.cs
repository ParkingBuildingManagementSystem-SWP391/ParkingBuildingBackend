using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class User
{
    public int UserId { get; set; }

    public string Username { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? PhoneNumber { get; set; }

    public string PasswordHash { get; set; } = null!;

    public int RoleId { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<IncidentReport> IncidentReportReporteds { get; set; } = new List<IncidentReport>();

    public virtual ICollection<IncidentReport> IncidentReportResolveds { get; set; } = new List<IncidentReport>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<MembershipCard> MembershipCards { get; set; } = new List<MembershipCard>();

    public virtual ICollection<ParkingSession> ParkingSessions { get; set; } = new List<ParkingSession>();

    public virtual Role Role { get; set; } = null!;

    public virtual ICollection<StaffActivityLog> StaffActivityLogs { get; set; } = new List<StaffActivityLog>();

    public virtual ICollection<StaffShift> StaffShifts { get; set; } = new List<StaffShift>();

    public virtual Wallet? Wallet { get; set; }
}
