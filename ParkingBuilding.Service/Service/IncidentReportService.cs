using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
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

            string? normalizedIssue = null;
            var input = dto.IssueType?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                throw new ArgumentException("Loại sự cố là bắt buộc.");
            }

            if (input.Equals("Lost Ticket", StringComparison.OrdinalIgnoreCase) || input.Equals("Mất thẻ xe", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.LostTicket;
            else if (input.Equals("Vehicle Damage", StringComparison.OrdinalIgnoreCase) || input.Equals("Hỏng xe", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.VehicleDamage;
            else if (input.Equals("Lost Property", StringComparison.OrdinalIgnoreCase) || input.Equals("Mất đồ", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.LostProperty;
            else if (input.Equals("Staff Attitude", StringComparison.OrdinalIgnoreCase) || input.Equals("Thái độ nhân viên", StringComparison.OrdinalIgnoreCase) || input.Equals("Thái đồ nhân viên", StringComparison.OrdinalIgnoreCase) || input.Equals("Staff Conduct", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.StaffAttitude;
            else if (input.Equals("Other", StringComparison.OrdinalIgnoreCase) || input.Equals("Khác", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.Other;
            else if (input.Equals("Equipment Malfunction", StringComparison.OrdinalIgnoreCase) || input.Equals("Lỗi thiết bị", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.EquipmentMalfunction;
            else if (input.Equals("Vehicle Collision", StringComparison.OrdinalIgnoreCase) || input.Equals("Va chạm xe", StringComparison.OrdinalIgnoreCase))
                normalizedIssue = IncidentTypes.VehicleCollision;

            if (normalizedIssue == null)
            {
                throw new ArgumentException($"Loại sự cố '{dto.IssueType}' không hợp lệ hoặc không được hỗ trợ.");
            }

            dto.IssueType = normalizedIssue;

            if (user == null)
            {
                throw new UnauthorizedAccessException("Tài khoản của bạn không tồn tại hoặc không hợp lệ.");
            }

            var roleName = user.Role?.RoleName;
            if (roleName == "Registered_Driver")
            {
                var allowedDriverTypes = new[] 
                { 
                    IncidentTypes.LostTicket, 
                    IncidentTypes.VehicleDamage, 
                    IncidentTypes.LostProperty, 
                    IncidentTypes.StaffAttitude, 
                    IncidentTypes.Other 
                };
                if (!allowedDriverTypes.Contains(normalizedIssue))
                {
                    throw new ArgumentException("Tài xế không có quyền chọn loại sự cố này.");
                }
            }
            else if (roleName == "Staff")
            {
                var allowedStaffTypes = new[] 
                { 
                    IncidentTypes.EquipmentMalfunction, 
                    IncidentTypes.LostTicket, 
                    IncidentTypes.VehicleCollision, 
                    IncidentTypes.LostProperty, 
                    IncidentTypes.Other 
                };
                if (!allowedStaffTypes.Contains(normalizedIssue))
                {
                    throw new ArgumentException("Nhân viên không có quyền chọn loại sự cố này.");
                }
            }
            else
            {
                throw new UnauthorizedAccessException("Bạn không có quyền báo cáo sự cố.");
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
            incident.FineAmount = dto.FineAmount ?? 0;

            bool isLostTicket = string.Equals(incident.IssueType, IncidentTypes.LostTicket, StringComparison.OrdinalIgnoreCase)
                             || string.Equals(incident.IssueType, "LostTicket", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(incident.IssueType, "Lost Ticket", StringComparison.OrdinalIgnoreCase);

            if (isLostTicket && incident.SessionId.HasValue)
            {
                var session = await _context.ParkingSessions
                    .Include(s => s.Ticket)
                    .Include(s => s.Type)
                    .Include(s => s.Invoice)
                    .FirstOrDefaultAsync(s => s.SessionId == incident.SessionId.Value);

                if (session != null && session.SessionStatus == ParkingStatuses.SessionInProgress)
                {
                    session.SessionStatus = ParkingStatuses.SessionCompleted;
                    session.CheckOutTime = DateTime.UtcNow;

                    if (session.Ticket != null)
                    {
                        session.Ticket.TicketStatus = "Blocked";
                    }

                    var slot = await _unitOfWork.Slots.GetByIdAsync(session.SlotId);
                    if (slot != null)
                    {
                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    }

                    // Tự động tính tiền đỗ xe theo giờ + Tiền phạt mất thẻ để chốt Tổng hóa đơn
                    decimal fine = incident.FineAmount ?? 0;
                    decimal parkingFee = 0;
                    if (session.CheckInTime.HasValue && session.Type != null)
                    {
                        parkingFee = ParkingPricingCalculator.CalculateFee(session.CheckInTime.Value, session.CheckOutTime.Value, session.Type);
                    }
                    decimal totalAmount = parkingFee + fine;

                    var invoice = session.Invoice;
                    if (invoice == null)
                    {
                        invoice = new Invoice
                        {
                            SessionId = session.SessionId,
                            TotalAmount = totalAmount,
                            PaymentMethod = "CASH",
                            PaymentStatus = "SUCCESS",
                            PaymentTime = DateTime.Now,
                            CreatedDate = DateTime.Now,
                            StaffId = resolvedUserId
                        };
                        await _context.Invoices.AddAsync(invoice);
                    }
                    else
                    {
                        invoice.TotalAmount = totalAmount;
                        invoice.PaymentStatus = "SUCCESS";
                        invoice.PaymentTime = DateTime.Now;
                        _context.Invoices.Update(invoice);
                    }
                }
            }

            var isSaved = await _context.SaveChangesAsync() >= 0;
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

            return true;
        }

        public async Task<IncidentStatisticsDto> GetIncidentStatisticsAsync()
        {
            var incidents = await _context.IncidentReports.ToListAsync();

            int total = incidents.Count;
            int pending = incidents.Count(i => i.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
            int resolved = incidents.Count(i => i.Status.Equals("Resolved", StringComparison.OrdinalIgnoreCase));
            decimal totalFine = incidents.Where(i => i.FineAmount.HasValue).Sum(i => i.FineAmount!.Value);

            string topIssue = incidents
                .GroupBy(i => i.IssueType)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? "N/A";

            return new IncidentStatisticsDto
            {
                TotalIncidents = total,
                PendingCount = pending,
                ResolvedCount = resolved,
                TotalFineCollected = totalFine,
                TopIssueType = topIssue
            };
        }

        private IncidentReportResponseDto MapToResponseDto(IncidentReport i)
        {
            var severity = i.IssueType switch
            {
                IncidentTypes.LostTicket or IncidentTypes.VehicleDamage or IncidentTypes.TicketMismatch or IncidentTypes.PlateMismatch or IncidentTypes.VehicleCollision => "Critical",
                IncidentTypes.EquipmentMalfunction or IncidentTypes.LostProperty => "Warning",
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
