using System;


using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class IncidentReportRepository : GenericRepository<IncidentReport>, IIncidentReportRepository
    {
        public IncidentReportRepository(ParkingManagementDbContext context) : base(context)
        {
        }

        // Lấy danh sách sự cố kèm lọc và thông tin liên quan
        public async Task<List<IncidentReport>> GetIncidentsWithFiltersAsync(
            string? status,
            string? issueType,
            string? licenseVehicle)
        {
            var query = _context.IncidentReports
                .Include(i => i.Session)
                .Include(i => i.Reported)
                .Include(i => i.Resolved)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(i => i.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(issueType))
            {
                query = query.Where(i => i.IssueType == issueType);
            }

            if (!string.IsNullOrWhiteSpace(licenseVehicle))
            {
                // Tìm những sự cố liên quan đến biển số xe được nhập
                query = query.Where(i => i.Session != null && i.Session.LicenseVehicle.Contains(licenseVehicle));
            }

            // Sắp xếp sự cố mới nhất lên đầu
            return await query.OrderByDescending(i => i.IncidentId).ToListAsync();
        }

        // Lấy chi tiết sự cố theo ID
        public async Task<IncidentReport?> GetIncidentDetailByIdAsync(int incidentId)
        {
            return await _context.IncidentReports
                .Include(i => i.Session)
                .Include(i => i.Reported)
                .Include(i => i.Resolved)
                .FirstOrDefaultAsync(i => i.IncidentId == incidentId);
        }
    }
}
