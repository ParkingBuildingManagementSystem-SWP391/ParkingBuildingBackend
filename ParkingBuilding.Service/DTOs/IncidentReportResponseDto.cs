using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class IncidentReportResponseDto
    {
        public int IncidentId { get; set; }
        public int? SessionId { get; set; }
        public string? LicenseVehicle { get; set; } // Biển số xe lấy từ Session
        public string IssueType { get; set; } = null!;
        public string? Description { get; set; }
        public string Status { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolutionNotes { get; set; }
        public decimal? FineAmount { get; set; }
        public string? ImageProofUrl { get; set; }
        public string Severity { get; set; } = null!; // Biển số xe lấy từ Session

        // Thông tin người báo cáo và giải quyết
        public int ReportedId { get; set; }
        public string ReportedUsername { get; set; } = null!;
        public int? ResolvedId { get; set; }
        public string? ResolvedUsername { get; set; }
    }
}
