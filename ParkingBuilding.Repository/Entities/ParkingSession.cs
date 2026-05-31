using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class ParkingSession
{
    public int SessionId { get; set; }

    public int? UserId { get; set; }

    public int SlotId { get; set; }

    public string LicenseVehicle { get; set; } = null!;

    public int TypeId { get; set; }

    public DateTime? BookingTime { get; set; }

    public DateTime? CheckInTime { get; set; }

    public DateTime? CheckOutTime { get; set; }

    public string? CheckInImageUrl { get; set; }

    public string? CheckOutImageUrl { get; set; }

    public string SessionStatus { get; set; } = null!;

    public int? TicketId { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<IncidentReport> IncidentReports { get; set; } = new List<IncidentReport>();

    public virtual Invoice? Invoice { get; set; }

    public virtual ParkingSlot Slot { get; set; } = null!;

    public virtual Ticket? Ticket { get; set; } = null!;

    public virtual VehiclesType Type { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
