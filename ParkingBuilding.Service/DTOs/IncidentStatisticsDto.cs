using System;
using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class IncidentStatisticsDto
    {
        public int TotalIncidents { get; set; }
        public int PendingCount { get; set; }
        public int ResolvedCount { get; set; }
        public decimal TotalFineCollected { get; set; }
        public string TopIssueType { get; set; } = "N/A";
    }
}
