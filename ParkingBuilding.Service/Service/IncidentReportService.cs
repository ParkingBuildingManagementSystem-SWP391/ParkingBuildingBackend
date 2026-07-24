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
        private readonly INotificationService _notificationService;

        public IncidentReportService(
            IIncidentReportRepository incidentRepo,
            IUnitOfWork unitOfWork,
            ParkingManagementDbContext context,
            INotificationService notificationService)
        {
            _incidentRepo = incidentRepo;
            _unitOfWork = unitOfWork;
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<IncidentReportResponseDto> CreateIncidentAsync(CreateIncidentReportDto dto, int reportedUserId)
        {
            var user = await _context.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.UserId == reportedUserId);

            // Staff members cannot submit staff conduct complaints
            if (user != null && user.Role?.RoleName == "Staff" && 
                (dto.IssueType.Equals("Staff Conduct", StringComparison.OrdinalIgnoreCase) || 
                 dto.IssueType.Equals("Thái độ nhân viên", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Staff members cannot submit staff conduct complaints.");
            }

            bool isEquipmentIncident = dto.IssueType.Equals(IncidentTypes.EquipmentMalfunction, StringComparison.OrdinalIgnoreCase);
            ParkingSession? session = null;

            if (!isEquipmentIncident || !string.IsNullOrWhiteSpace(dto.LicenseVehicle))
            {
                if (string.IsNullOrWhiteSpace(dto.LicenseVehicle))
                {
                    throw new ArgumentException("License plate is required for vehicle or ticket related incidents.");
                }

                var normalizedLicense = dto.LicenseVehicle.Trim().ToUpper();
                session = await _context.ParkingSessions
                    .Where(s => s.LicenseVehicle.Trim().ToUpper() == normalizedLicense
                             && (s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress 
                              || s.SessionStatus.Trim() == ParkingStatuses.SessionCompleted)
                             && !s.IsDeleted)
                    .OrderByDescending(s => s.CheckInTime)
                    .ThenByDescending(s => s.SessionId)
                    .FirstOrDefaultAsync();

                if (session == null && !isEquipmentIncident)
                {
                    throw new ArgumentException("No active or recently completed parking session was found for this license plate.");
                }

                if (user != null && user.Role?.RoleName == "Registered_Driver" && session != null && session.UserId != reportedUserId)
                {
                    throw new UnauthorizedAccessException("You do not have permission to report an incident for another driver's parking session.");
                }
            }

            var incident = new IncidentReport
            {
                SessionId = session?.SessionId,
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

        public async Task<List<IncidentReportResponseDto>> GetMyIncidentsAsync(int userId)
        {
            var incidents = await _context.IncidentReports
                .Include(i => i.Session)
                .Include(i => i.Reported)
                .Include(i => i.Resolved)
                .Where(i => i.ReportedId == userId)
                .OrderByDescending(i => i.IncidentId)
                .ToListAsync();

            return incidents.Select(MapToResponseDto).ToList();
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

            if (incident.IssueType.Equals(IncidentTypes.LostTicket, StringComparison.OrdinalIgnoreCase) && incident.SessionId.HasValue)
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

            var isSaved = await _unitOfWork.SaveChangesAsync();
            if (isSaved)
            {
                try
                {
                    var resolver = await _context.Users.FirstOrDefaultAsync(u => u.UserId == resolvedUserId);
                    string resolverName = resolver?.Username ?? "Quản lý";
                    string title = $"Sự cố #{incident.IncidentId} đã được giải quyết";
                    string content = $"Sự cố '{incident.IssueType}' do bạn báo cáo đã được giải quyết bởi {resolverName}. Ghi chú: {dto.ResolutionNotes}";
                    await _notificationService.SendToUserAsync(incident.ReportedId, title, content, NotificationTypes.IncidentResolved);
                }
                catch
                {
                    // Ignore notification exceptions so transaction result is not affected
                }
            }

            return isSaved;
        }

        private IncidentReportResponseDto MapToResponseDto(IncidentReport i)
        {
            var severity = i.IssueType switch
            {
                IncidentTypes.LostTicket or IncidentTypes.VehicleDamage or IncidentTypes.TicketMismatch or IncidentTypes.PlateMismatch => "Critical",
                IncidentTypes.EquipmentMalfunction => "Warning",
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
