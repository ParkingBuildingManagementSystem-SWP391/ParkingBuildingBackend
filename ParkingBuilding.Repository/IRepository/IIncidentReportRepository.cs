using System;
using System.Linq;
using System.Text;
using ParkingBuilding.Repository.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IIncidentReportRepository : IGenericRepository<IncidentReport>
    {
        Task<List<IncidentReport>> GetIncidentsWithFiltersAsync(
            string? status,
            string? issueType,
            string? licenseVehicle,
            string? severity);

        Task<IncidentReport?> GetIncidentDetailByIdAsync(int incidentId);
    }
}
