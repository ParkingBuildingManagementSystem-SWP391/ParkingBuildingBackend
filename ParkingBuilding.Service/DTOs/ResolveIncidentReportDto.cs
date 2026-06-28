using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class ResolveIncidentReportDto
    {
        public string? ResolutionNotes { get; set; }
        public decimal? FineAmount { get; set; }
    }
}
