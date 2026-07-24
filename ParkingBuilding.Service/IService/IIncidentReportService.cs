using System;

using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IIncidentReportService
    {
        Task<IncidentReportResponseDto> CreateIncidentAsync(CreateIncidentReportDto dto, int reportedUserId);
        Task<List<IncidentReportResponseDto>> GetIncidentsAsync(string? status, string? issueType, string? licenseVehicle, string? severity);
        Task<IncidentReportResponseDto?> GetIncidentByIdAsync(int incidentId);
        Task<List<IncidentReportResponseDto>> GetMyIncidentsAsync(int userId);
        Task<bool> ResolveIncidentAsync(int incidentId, ResolveIncidentReportDto dto, int resolvedUserId);
    }
}
