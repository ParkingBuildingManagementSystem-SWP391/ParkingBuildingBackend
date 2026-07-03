using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface ISessionRepository : IGenericRepository<ParkingSession>
    {
        // API 1: Lấy danh sách không điều kiện
        Task<List<ParkingSession>> GetAllSessionsWithDetailsAsync();

        // API 2: Tìm kiếm theo nhiều bộ lọc điều kiện
        Task<List<ParkingSession>> GetSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            int? isRegistered,
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate);

        // API 3: Lấy chi tiết phiên đỗ dựa vào mã vé
        Task<ParkingSession?> GetSessionDetailByTicketCodeAsync(string ticketCode);

        // API 4: Lấy chi tiết phiên đỗ dựa vào SessionId
        Task<ParkingSession?> GetSessionDetailByIdAsync(int sessionId);
    }
}
