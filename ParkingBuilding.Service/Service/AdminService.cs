using Microsoft.Extensions.Logging;
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
    public class AdminService : IAdminService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<AdminService> _logger;

        public AdminService(IUnitOfWork unitOfWork, ILogger<AdminService> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task<bool> updateUserAsync(UpdateUserRequestDto request)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(request.UserId);
            if (user == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy tài khoản người dùng với ID: {request.UserId}");
            }

            if (!string.IsNullOrEmpty(request.RoleName))
            {
                var role = await _unitOfWork.Users.GetRoleByNameAsync(request.RoleName);
                if (role == null)
                {
                    throw new ArgumentException($"Hệ thống không tồn tại phân quyền có tên: '{request.RoleName}'. Vui lòng kiểm tra lại!");
                }

                user.RoleId = role.RoleId;
            }

            if (!string.IsNullOrEmpty(request.phoneNumber))
            {
                user.PhoneNumber = request.phoneNumber;
            }

            if (!string.IsNullOrEmpty(request.userName))
            {
                user.Username = request.userName;
            }

            if (!string.IsNullOrEmpty(request.email))
            {
                user.Email = request.email;
            }

            return await _unitOfWork.SaveChangesAsync();
        }

        public async Task<UserResponseDto> CreateUserAsync(CreateUserRequestDto request)
        {
            var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (existingUser != null)
            {
                throw new ArgumentException("Email này đã được sử dụng bởi tài khoản khác trong hệ thống.");
            }

            var role = await _unitOfWork.Users.GetRoleByNameAsync(request.RoleName);
            if (role == null)
            {
                throw new KeyNotFoundException($"Không tìm thấy vai trò '{request.RoleName}' trong hệ thống.");
            }

            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(request.Password);
            var newUser = new User
            {
                Username = request.Username,
                Email = request.Email,
                PhoneNumber = request.PhoneNumber,
                PasswordHash = hashedPassword,
                RoleId = role.RoleId,
                IsDeleted = false
            };

            await _unitOfWork.Users.AddAsync(newUser);
            var isSuccess = await _unitOfWork.SaveChangesAsync();
            if (!isSuccess)
            {
                throw new Exception("Lỗi hệ thống! Không thể tạo tài khoản vào lúc này.");
            }

            return new UserResponseDto
            {
                Id = newUser.UserId,
                Name = newUser.Username,
                Email = newUser.Email,
                PhoneNumber = newUser.PhoneNumber,
                Role = role.RoleName
            };
        }

        public async Task<IEnumerable<UserResponseDto>> GetAllUsersAsync()
        {
            var users = await _unitOfWork.Users.GetAllUsersWithRolesAsync();

            return users.Select(u => new UserResponseDto
            {
                Id = u.UserId,
                Name = u.Username,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Role = u.Role.RoleName
            });
        }

        public async Task<List<ParkingSessionResponeDto>> GetAllParkingSessionsAsync()
        {
            _logger.LogInformation("Executing GetAllParkingSessionsAsync in Service.");

            var sessions = await _unitOfWork.Sessions.GetAllSessionsWithDetailsAsync();
            return sessions.Select(MapSessionListItem).ToList();
        }

        public async Task<List<ParkingSessionResponeDto>> GetParkingSessionsWithFiltersAsync(
            string? licenseVehicle,
            string? slotName,
            int? isRegistered,
            int? typeId,
            string? sessionStatus,
            DateTime? fromDate,
            DateTime? toDate)
        {
            _logger.LogInformation(
                "Executing GetParkingSessionsWithFiltersAsync. isRegistered={IsRegistered}, fromDate={FromDate}, toDate={ToDate}",
                isRegistered, fromDate, toDate);

            var sessions = await _unitOfWork.Sessions.GetSessionsWithFiltersAsync(
                licenseVehicle, slotName, isRegistered, typeId, sessionStatus, fromDate, toDate);

            return sessions.Select(MapSessionListItem).ToList();
        }

        public async Task<ParkingSessionDetailResponeDto?> GetSessionDetailByTicketCodeAsync(string ticketCode)
        {
            if (Helpers.QrCodeParserHelper.TryParseQr(ticketCode, out var parsedTicket, out var parsedPlate, out var parsedSessionId, out var parsedSlot))
            {
                ticketCode = parsedTicket!;
            }

            var session = await _unitOfWork.Sessions.GetSessionDetailByTicketCodeAsync(ticketCode);
            if (session == null)
            {
                return null;
            }

            return MapSessionDetail(session);
        }

        public async Task<ParkingSessionDetailResponeDto?> GetSessionDetailByIdAsync(int sessionId)
        {
            var session = await _unitOfWork.Sessions.GetSessionDetailByIdAsync(sessionId);
            if (session == null)
            {
                return null;
            }

            return MapSessionDetail(session);
        }

        private ParkingSessionResponeDto MapSessionListItem(ParkingSession session)
        {
            return new ParkingSessionResponeDto
            {
                SessionId = session.SessionId,
                LicenseVehicle = session.LicenseVehicle,
                SlotName = session.Slot?.SlotName ?? "Không xác định",
                TypeId = session.TypeId,
                TicketCode = session.Ticket?.TicketCode,
                SessionStatus = session.SessionStatus.Trim()
            };
        }

        private ParkingSessionDetailResponeDto MapSessionDetail(ParkingSession session)
        {
            return new ParkingSessionDetailResponeDto
            {
                SessionId = session.SessionId,
                Username = session.User?.Username ?? "Khách vãng lai",
                SlotName = session.Slot?.SlotName ?? "N/A",
                LicenseVehicle = session.LicenseVehicle,
                TypeId = session.TypeId,
                BookingTime = session.BookingTime,
                CheckInTime = session.CheckInTime,
                CheckOutTime = session.CheckOutTime,
                CheckInImageUrl = session.CheckInImageUrl,
                CheckOutImageUrl = session.CheckOutImageUrl,
                SessionStatus = session.SessionStatus.Trim(),
                Incidents = session.IncidentReports
                    .OrderByDescending(i => i.IncidentId)
                    .Select(i => new IncidentReportResponseDto
                    {
                        IncidentId = i.IncidentId,
                        SessionId = i.SessionId,
                        LicenseVehicle = session.LicenseVehicle,
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
                    })
                    .ToList()
            };
        }
    }
}
