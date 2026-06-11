using System;

namespace ParkingBuilding.Service.DTOs
{
    public class TrafficStatsRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string GroupBy { get; set; } = "DAY"; // "HOUR", "DAY"
        public int? VehicleTypeId { get; set; }
    }

    public class TrafficStatsResponse
    {
        public string TimeLabel { get; set; } = string.Empty;
        public int CheckInCount { get; set; }
        public int CheckOutCount { get; set; }
        public decimal RevenueGenerated { get; set; }
    }
}
