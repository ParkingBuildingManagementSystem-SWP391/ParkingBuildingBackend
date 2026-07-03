using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CreateIncidentReportDto
    {
        public string LicenseVehicle { get; set; } = null!;
        public string IssueType { get; set; } = null!;
        public string? Description { get; set; }
        public string? ImageProofUrl { get; set; }
    }
}
