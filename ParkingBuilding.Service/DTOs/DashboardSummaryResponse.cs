using System;
using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class DashboardSummaryResponse
    {
        public DateTime GeneratedTime { get; set; }
        public int TotalSlotsCount { get; set; }
        public int OccupiedSlotsCount { get; set; }
        public int ReservedSlotsCount { get; set; }
        public int AvailableSlotsCount { get; set; }
        public double OccupancyRate { get; set; } // (Occupied / Total) * 100

        public decimal TodayRevenue { get; set; }
        public decimal TotalRevenue { get; set; }

        public List<VehicleTypeCountDto> VehiclesInBuildingDetail { get; set; } = new();
        public List<FloorStatusDto> FloorOccupancyDetail { get; set; } = new();
    }

    public class VehicleTypeCountDto
    {
        public string VehicleTypeName { get; set; } = string.Empty;
        public int InBuildingCount { get; set; }
    }

    public class FloorStatusDto
    {
        public int FloorId { get; set; }
        public string FloorName { get; set; } = string.Empty;
        public int Capacity { get; set; }
        public int OccupiedCount { get; set; }
        public double OccupancyRate => Capacity > 0 ? Math.Round((double)OccupiedCount / Capacity * 100, 2) : 0;
    }
}
