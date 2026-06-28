using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class ParkingQueryService : IParkingQueryService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ILogger<ParkingQueryService> _logger;

        public ParkingQueryService(
            IParkingRepository parkingRepository,
            ILogger<ParkingQueryService> logger)
        {
            _parkingRepository = parkingRepository;
            _logger = logger;
        }

        public async Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId)
        {
            var slots = await _parkingRepository.GetSlotsByFloorIdAsync(floorId);

            return slots.Select(s => new ParkingSlotResponseDto
            {
                SlotId = s.SlotId,
                SlotName = s.SlotName,
                SlotStatus = s.SlotStatus,
                TypeId = s.TypeId
            }).ToList();
        }

        public async Task<MyBookingsDashboardDto> GetMyBookingsAsync(int userId)
        {
            _logger.LogInformation("Bắt đầu lấy danh sách phiên đỗ xe của người dùng {UserId} từ Repository.", userId);

            var sessions = await _parkingRepository.GetSessionsByUserIdAsync(userId);

            _logger.LogInformation("Đã tìm thấy {Count} phiên đỗ xe của người dùng {UserId} từ database.", sessions.Count, userId);

            var bookingsList = sessions.Select(s => new MyBookingResponseDto
            {
                SessionId = s.SessionId,
                TypeId = s.TypeId,
                BookingTime = s.BookingTime,
                SessionStatus = s.SessionStatus.Trim(),
                FloorName = s.Slot?.Floor?.FloorName ?? "N/A",
                SlotName = s.Slot?.SlotName ?? "N/A",
                LicenseVehicle = s.LicenseVehicle,
                TicketCode = s.Ticket?.TicketCode,
                CheckInTime = s.CheckInTime,
                CheckOutTime = s.CheckOutTime,
                TotalAmount = s.Invoice?.TotalAmount,
                PaymentStatus = s.Invoice?.PaymentStatus,
                PaymentMethod = s.Invoice?.PaymentMethod
            }).ToList();

            var dashboard = new MyBookingsDashboardDto
            {
                TotalBookings = bookingsList.Count,
                ActiveBookings = bookingsList.Count(b => b.SessionStatus == "Reserved" || b.SessionStatus == "InProgress"),
                CompletedBookings = bookingsList.Count(b => b.SessionStatus == "Completed"),
                CanceledBookings = bookingsList.Count(b => b.SessionStatus == "Canceled"),
                TotalAmountSpent = bookingsList
                    .Where(b => b.PaymentStatus == "SUCCESS" && b.TotalAmount.HasValue)
                    .Sum(b => b.TotalAmount!.Value),
                BookingsList = bookingsList
            };

            _logger.LogInformation("Thống kê thành công cho người dùng {UserId}. Tổng số tiền chi tiêu: {TotalAmountSpent} VND.", userId, dashboard.TotalAmountSpent);

            return dashboard;
        }

        public async Task<List<ActiveSessionResponseDto>> GetActiveSessionsAsync()
        {
            var sessions = await _parkingRepository.GetActiveSessionsAsync();
            return sessions.Select(s => new ActiveSessionResponseDto
            {
                SessionId = s.SessionId,
                LicenseVehicle = s.LicenseVehicle,
                TicketCode = s.Ticket?.TicketCode,
                SessionStatus = s.SessionStatus.Trim(),
                VehicleTypeName = s.Type?.TypeName ?? "Unknown",
                SlotId = s.SlotId,
                SlotName = s.Slot?.SlotName ?? "Unknown"
            }).ToList();
        }

        public async Task<LocateVehicleResponseDto?> LocateVehicleAsync(string licensePlate)
        {
            if (string.IsNullOrWhiteSpace(licensePlate))
            {
                throw new ArgumentException("Biển số xe không được để trống.");
            }
            // 1. Chuẩn hóa biển số xe nhập vào (Ví dụ: "30A - 123.45" -> "30A12345")
            string cleanedPlate = Regex.Replace(licensePlate, @"[.\-\s]", "").ToUpper();
            _logger.LogInformation("Tìm vị trí đỗ cho biển số đã chuẩn hóa: {LicensePlate}", cleanedPlate);
            // 2. Tìm phiên đỗ xe InProgress
            var session = await _parkingRepository.GetActiveSessionByLicensePlateWithFloorAsync(cleanedPlate);
            if (session == null)
            {
                _logger.LogWarning("Không tìm thấy xe {LicensePlate} đang đỗ trong hệ thống.", cleanedPlate);
                return null;
            }
            // 3. Trả về thông tin ánh xạ tương ứng
            return new LocateVehicleResponseDto
            {
                LicenseVehicle = session.LicenseVehicle,
                SlotName = session.Slot?.SlotName ?? "Chưa gán vị trí",
                FloorName = session.Slot?.Floor?.FloorName ?? "Không xác định",
                FloorId = session.Slot?.FloorId ?? 0,
                CheckInTime = session.CheckInTime ?? DateTime.MinValue,
                CheckInImageUrl = session.CheckInImageUrl
            };
        }

        public async Task<List<ParkingSlotResponseDto>> GetSlotsAsync(int? typeId, string? status)
        {
            var slots = await _parkingRepository.GetSlotsAsync(typeId, status);

            return slots.Select(s => new ParkingSlotResponseDto
            {
                SlotId = s.SlotId,
                SlotName = s.SlotName,
                SlotStatus = s.SlotStatus,
                TypeId = s.TypeId
            }).ToList();
        }
    }
}
