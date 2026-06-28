using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            var incident = new IncidentReport
            {
                SessionId = dto.SessionId,
                IssueType = dto.IssueType,
                Description = dto.Description,
                ImageProofUrl = dto.ImageProofUrl,
                ReportedId = reportedUserId,
                Status = "Pending",
                CreatedAt = DateTime.Now
            };

            await _incidentRepo.AddAsync(incident);
            await _unitOfWork.SaveChangesAsync();

            // Lấy lại thông tin chi tiết kèm nạp nốt các bảng liên quan để map sang DTO
            var createdIncident = await _incidentRepo.GetIncidentDetailByIdAsync(incident.IncidentId);
            return MapToResponseDto(createdIncident!);
        }

        public async Task<List<IncidentReportResponseDto>> GetIncidentsAsync(string? status, string? issueType, string? licenseVehicle)
        {
            var incidents = await _incidentRepo.GetIncidentsWithFiltersAsync(status, issueType, licenseVehicle);
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
            incident.FineAmount = dto.FineAmount ?? 0;

            // XỬ LÝ NGHIỆP VỤ ĐẶC BIỆT: Nếu là mất thẻ (Lost Ticket) và có phiên đỗ xe
            if (incident.IssueType.Equals("Lost Ticket", StringComparison.OrdinalIgnoreCase) && incident.SessionId.HasValue)
            {
                // Tìm phiên đỗ xe để đóng lượt gửi (Checkout)
                var session = await _unitOfWork.Sessions.GetByIdAsync(incident.SessionId.Value);
                if (session != null && session.SessionStatus == ParkingStatuses.SessionInProgress)
                {
                    session.SessionStatus = ParkingStatuses.SessionCompleted;
                    session.CheckOutTime = DateTime.Now;

                    // 1. Khóa thẻ lại vì đã bị mất (không cho phép quét thẻ này nữa)
                    if (session.Ticket != null)
                    {
                        session.Ticket.TicketStatus = "Blocked";
                    }

                    // 2. Giải phóng chỗ đỗ (Parking Slot)
                    var slot = await _unitOfWork.Slots.GetByIdAsync(session.SlotId);
                    if (slot != null)
                    {
                        // Kiểm tra nếu ô đỗ này có thẻ tháng đăng ký hoạt động
                        var hasActiveMonthly = await _context.MonthlyCards.AnyAsync(mc => mc.SlotId == slot.SlotId && mc.Status == ParkingStatuses.MonthlyCardActive && !mc.IsDeleted);
                        slot.SlotStatus = hasActiveMonthly ? ParkingStatuses.SlotReserved : ParkingStatuses.SlotAvailable;
                    }

                    // 3. TẠO HÓA ĐƠN THU TIỀN PHẠT MẤT THẺ ĐỂ ĐỐI SOÁT DOANH THU
                    var invoice = new Invoice
                    {
                        SessionId = session.SessionId,
                        TotalAmount = dto.FineAmount ?? 0,
                        PaymentTime = DateTime.Now,
                        StaffId = resolvedUserId,
                        PaymentMethod = "CASH",
                        PaymentStatus = "SUCCESS",
                        CreatedDate = DateTime.Now
                    };
                    await _unitOfWork.Invoices.AddAsync(invoice);
                }
            }

            return await _unitOfWork.SaveChangesAsync();
        }

        // Helper Map Entity sang Response DTO
        private IncidentReportResponseDto MapToResponseDto(IncidentReport i)
        {
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
                ResolvedUsername = i.Resolved?.Username
            };
        }
    }
}
