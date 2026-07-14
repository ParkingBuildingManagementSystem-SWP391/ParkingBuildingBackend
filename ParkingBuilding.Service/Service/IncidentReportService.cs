using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class IncidentReportService : IIncidentReportService
    {
        private readonly IIncidentReportRepository _incidentRepo;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ParkingManagementDbContext _context;

        public IncidentReportService(
            IIncidentReportRepository incidentRepo,
            IUnitOfWork unitOfWork,
            ParkingManagementDbContext context)
        {
            _incidentRepo = incidentRepo;
            _unitOfWork = unitOfWork;
            _context = context;
        }

        public async Task<IncidentReportResponseDto> CreateIncidentAsync(CreateIncidentReportDto dto, int reportedUserId)
        {
            if (string.IsNullOrWhiteSpace(dto.LicenseVehicle))
            {
                throw new ArgumentException("License plate is required.");
            }

            var normalizedLicense = dto.LicenseVehicle.Trim().ToUpper();
            var session = await _context.ParkingSessions
                .Where(s => s.LicenseVehicle.Trim().ToUpper() == normalizedLicense
                         && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                         && !s.IsDeleted)
                .OrderByDescending(s => s.CheckInTime)
                .ThenByDescending(s => s.SessionId)
                .FirstOrDefaultAsync();

            if (session == null)
            {
                throw new ArgumentException("No active parking session was found for this license plate.");
            }

            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == reportedUserId);
            if (user != null && user.Role?.RoleName == "Registered_Driver" && session.UserId != reportedUserId)
            {
                throw new UnauthorizedAccessException("You do not have permission to report an incident for another driver's parking session.");
            }

            var incident = new IncidentReport
            {
                SessionId = session.SessionId,
                IssueType = dto.IssueType,
                Description = dto.Description,
                ImageProofUrl = dto.ImageProofUrl,
                ReportedId = reportedUserId,
                Status = "Pending",
                CreatedAt = DateTime.Now
            };

            await _incidentRepo.AddAsync(incident);
            await _unitOfWork.SaveChangesAsync();

            var createdIncident = await _incidentRepo.GetIncidentDetailByIdAsync(incident.IncidentId);
            return MapToResponseDto(createdIncident!);
        }

        public async Task<List<IncidentReportResponseDto>> GetIncidentsAsync(string? status, string? issueType, string? licenseVehicle, string? severity)
        {
            var incidents = await _incidentRepo.GetIncidentsWithFiltersAsync(status, issueType, licenseVehicle, severity);
            return incidents.Select(MapToResponseDto).ToList();
        }

        public async Task<IncidentReportResponseDto?> GetIncidentByIdAsync(int incidentId)
        {
            var incident = await _incidentRepo.GetIncidentDetailByIdAsync(incidentId);
            if (incident == null) return null;
            return MapToResponseDto(incident);
        }

        public async Task<bool> ResolveIncidentAsync(int incidentId, ResolveIncidentReportDto dto, int resolvedUserId)
        {
            var incident = await _incidentRepo.GetByIdAsync(incidentId);
            if (incident == null || incident.Status == "Resolved")
            {
                return false;
            }

            incident.Status = "Resolved";
            incident.ResolvedId = resolvedUserId;
            incident.ResolvedAt = DateTime.Now;
            incident.ResolutionNotes = dto.ResolutionNotes;
            incident.FineAmount = 0;

            if (incident.IssueType.Equals("Lost Ticket", StringComparison.OrdinalIgnoreCase) && incident.SessionId.HasValue)
            {
                var session = await _unitOfWork.Sessions.GetByIdAsync(incident.SessionId.Value);
                if (session != null && session.SessionStatus == ParkingStatuses.SessionInProgress)
                {
                    session.SessionStatus = ParkingStatuses.SessionCompleted;
                    session.CheckOutTime = DateTime.Now;

                    if (session.Ticket != null)
                    {
                        session.Ticket.TicketStatus = "Blocked";
                    }

                    var slot = await _unitOfWork.Slots.GetByIdAsync(session.SlotId);
                    if (slot != null)
                    {
                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    }
                }
            }

            return await _unitOfWork.SaveChangesAsync();
        }

        private IncidentReportResponseDto MapToResponseDto(IncidentReport i)
        {
            var severity = i.IssueType switch
            {
                "Lost Ticket" or "Vehicle Damage" => "Critical",
                "Equipment Malfunction" => "Warning",
                _ => "Info"
            };

            return new IncidentReportResponseDto
            {
                IncidentId = i.IncidentId,
                SessionId = i.SessionId,
                LicenseVehicle = i.Session?.LicenseVehicle,
                IssueType = i.IssueType,
                Description = i.Description,
                Status = i.Status,
                CreatedAt = i.CreatedAt,
                ResolvedAt = i.ResolvedAt,
                ResolutionNotes = i.ResolutionNotes,
                FineAmount = i.FineAmount,
                ImageProofUrl = i.ImageProofUrl,
                ReportedId = i.ReportedId,
                ReportedUsername = i.Reported?.Username ?? "N/A",
                ResolvedId = i.ResolvedId,
                ResolvedUsername = i.Resolved?.Username,
                Severity = severity
            };
        }
    }
}
