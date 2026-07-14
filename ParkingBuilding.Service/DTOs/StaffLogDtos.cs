using System;

namespace ParkingBuilding.Service.DTOs
{
    public class StartShiftResponse
    {
        public int ShiftId { get; set; }
        public int StaffId { get; set; }
        public DateTime StartTime { get; set; }
        public string Status { get; set; } = null!;
    }

    public class EndShiftRequest
    {
        public decimal ActualCash { get; set; }
        public string? Notes { get; set; }
    }

    public class EndShiftResponse
    {
        public int ShiftId { get; set; }
        public DateTime EndTime { get; set; }
        public decimal SystemCash { get; set; }
        public decimal ActualCash { get; set; }
        public decimal Difference { get; set; }
        public int TotalTransactions { get; set; }
        public string Status { get; set; } = null!;
    }

    public class StaffShiftDto
    {
        public int ShiftId { get; set; }
        public int StaffId { get; set; }
        public string StaffUsername { get; set; } = null!;
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public decimal SystemCash { get; set; }
        public decimal? ActualCash { get; set; }
        public decimal? Difference { get; set; }
        public int TotalTransactions { get; set; }
        public string Status { get; set; } = null!;
        public string? Notes { get; set; }
    }

    public class StaffActivityLogDto
    {
        public long LogId { get; set; }
        public int StaffId { get; set; }
        public string StaffUsername { get; set; } = null!;
        public int? ShiftId { get; set; }
        public string ActionType { get; set; } = null!;
        public DateTime Timestamp { get; set; }
        public string? LicensePlate { get; set; }
        public int? SessionId { get; set; }
        public string Description { get; set; } = null!;
        public string? IpAddress { get; set; }
    }
}
