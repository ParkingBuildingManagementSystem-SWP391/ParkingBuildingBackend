using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class MembershipCardService : IMembershipCardService
    {
        private readonly ParkingManagementDbContext _context;
        private readonly IVnPayService _vnPayService;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<MembershipCardService> _logger;
        private readonly IMemoryCache _cache;

        public MembershipCardService(
            ParkingManagementDbContext context,
            IVnPayService vnPayService,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<MembershipCardService> logger,
            IMemoryCache cache)
        {
            _context = context;
            _vnPayService = vnPayService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _cache = cache;
        }

        public async Task<MembershipCardRegistrationResponseDto> RegisterMembershipCardAsync(int userId, RegisterMembershipCardDto dto, string ipAddress)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Xác thực tài xế tồn tại
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted)
                           ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài xế.");

                // 2. Xác thực gói cước thành viên (Tier)
                var tier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == dto.TierId && !t.IsDeleted)
                             ?? throw new KeyNotFoundException("Gói cước thành viên không tồn tại hoặc đã bị xóa.");

                // 3. Kiểm tra tính hợp lệ của DTO so với Tier
                if (tier.DurationMonths != 1 && tier.DurationMonths != 6 && tier.DurationMonths != 12)
                {
                    throw new ArgumentException("Thời hạn thuê của gói thành viên không hợp lệ (chỉ được phép là 1, 6 hoặc 12 tháng).");
                }

                // 4. Kiểm tra biển số xe và giới hạn tối đa của gói cước
                if (dto.LicenseVehicles == null || dto.LicenseVehicles.Count == 0)
                {
                    throw new ArgumentException("Vui lòng cung cấp ít nhất một biển số xe.");
                }

                if (dto.LicenseVehicles.Count > tier.MaxVehicles)
                {
                    throw new ArgumentException($"Gói thành viên này chỉ cho phép đăng ký tối đa {tier.MaxVehicles} biển số xe.");
                }

                // Chuẩn hóa và validate từng biển số xe
                var cleanPlates = new List<string>();
                foreach (var plate in dto.LicenseVehicles)
                {
                    if (string.IsNullOrWhiteSpace(plate))
                    {
                        throw new ArgumentException("Biển số xe không được để trống.");
                    }

                    string cleanPlate = plate.Trim().ToUpper();
                    if (tier.TypeId != 1) // Không phải xe đạp (TypeId = 1) thì validate biển số xe
                    {
                        if (!LicensePlateHelper.IsValidLicensePlate(cleanPlate, out string validatedPlate))
                        {
                            throw new ArgumentException($"Biển số xe '{cleanPlate}' không hợp lệ: {LicensePlateHelper.GetErrorMessage()}");
                        }
                        cleanPlate = validatedPlate;
                    }
                    cleanPlates.Add(cleanPlate);
                }

                // 5. Xác thực và khóa ô đỗ xe (Slot)
                var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == dto.SlotId && !s.IsDeleted)
                           ?? throw new KeyNotFoundException("Chỗ đỗ xe không tồn tại hoặc đã bị xóa.");

                if (slot.SlotStatus != ParkingStatuses.SlotAvailable)
                {
                    throw new ArgumentException("Chỗ đỗ xe được chọn hiện tại không trống.");
                }

                if (slot.TypeId != tier.TypeId)
                {
                    throw new ArgumentException("Loại ô đỗ xe không khớp với loại xe của gói thành viên.");
                }

                // 6. Tính toán số tiền thanh toán và thời hạn sử dụng
                decimal amountToPay = tier.Price;
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var startTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                var endTime = startTime.AddMonths(tier.DurationMonths);

                // 7. Tạo mã vé cho Membership Card
                string ticketCode = $"MBC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

                // 8. Tạo mã giao dịch chứa SlotId để dễ dàng rollback khi hết hạn / thất bại
                string txnRef = $"MBC_{slot.SlotId}_{DateTime.UtcNow.Ticks}";

                // 9. Khóa ô đỗ xe ngay lập tức bằng trạng thái Reserved
                slot.SlotStatus = ParkingStatuses.SlotReserved;
                _context.ParkingSlots.Update(slot);

                // 10. Tạo Invoice PENDING
                var invoice = new Invoice
                {
                    SessionId = null,
                    TotalAmount = amountToPay,
                    PaymentMethod = "VNPAY",
                    PaymentStatus = "PENDING",
                    TransactionCode = txnRef,
                    CreatedDate = startTime,
                };
                await _context.Invoices.AddAsync(invoice);
                await _context.SaveChangesAsync();

                // 11. Lưu metadata tạm thời vào RAM cache (15 phút)
                var metadata = new MembershipRegistrationMetadata
                {
                    UserId = userId,
                    TierId = tier.TierId,
                    SlotId = slot.SlotId,
                    DurationMonths = tier.DurationMonths,
                    TicketCode = ticketCode,
                    LicenseVehicles = cleanPlates,
                    StartTime = startTime,
                    EndTime = endTime
                };
                _cache.Set(txnRef, metadata, TimeSpan.FromMinutes(15));

                // 12. Tạo URL thanh toán VNPay
                string paymentUrl = _vnPayService.CreatePaymentUrl(
                    txnRef: txnRef,
                    amount: amountToPay,
                    orderInfo: $"DK the thanh vien goi {tier.TierName} cho {tier.DurationMonths} thang",
                    returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                    ipAddress: ipAddress
                );

                await transaction.CommitAsync();

                return new MembershipCardRegistrationResponseDto
                {
                    Username = user.Username,
                    TicketCode = ticketCode,
                    AmountToPay = amountToPay,
                    SlotId = slot.SlotId,
                    LicenseVehicles = cleanPlates,
                    EndTime = endTime,
                    PaymentUrl = paymentUrl
                };
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PaymentResultDto> ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            // Sử dụng transaction bao bọc tất cả thao tác lưu DB
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Xác thực Invoice tồn tại và trạng thái PENDING
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Hóa đơn không tồn tại" };
                }

                if (invoice.TotalAmount != amount)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "Số tiền thanh toán không khớp" };
                }

                if (invoice.PaymentStatus == "SUCCESS" || invoice.PaymentStatus == "FAILED")
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hóa đơn đã được xử lý" };
                }

                // Phân tích cú pháp để tìm SlotId dự phòng từ txnRef (MBC_{SlotId}_{Ticks})
                int targetSlotId = 0;
                var parts = txnRef.Split('_');
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[1], out targetSlotId);
                }

                // 2. Xử lý khi thanh toán thất bại
                if (responseCode != "00" || transactionStatus != "00")
                {
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;

                    // Mở khóa slot đỗ xe
                    if (targetSlotId > 0)
                    {
                        var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == targetSlotId);
                        if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                        {
                            slot.SlotStatus = ParkingStatuses.SlotAvailable;
                            _context.ParkingSlots.Update(slot);
                        }
                    }

                    _cache.Remove(txnRef);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ cổng VNPay" };
                }

                // 3. Lấy dữ liệu tạm từ Cache
                if (!_cache.TryGetValue(txnRef, out MembershipRegistrationMetadata? meta) || meta == null)
                {
                    // Trường hợp hết hạn cache 15p: Revert slot đỗ về Available và cập nhật Invoice FAILED
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;

                    if (targetSlotId > 0)
                    {
                        var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == targetSlotId);
                        if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                        {
                            slot.SlotStatus = ParkingStatuses.SlotAvailable;
                            _context.ParkingSlots.Update(slot);
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "03", Message = "Yêu cầu đăng ký đã hết hạn (quá 15 phút)" };
                }

                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var currentDbTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

                // 4. Tạo Ticket
                var ticket = new Ticket
                {
                    TicketCode = meta.TicketCode,
                    TicketStatus = ParkingStatuses.TicketActive
                };
                await _context.Tickets.AddAsync(ticket);
                await _context.SaveChangesAsync(); // Lưu để lấy TicketId

                // 5. Tạo MembershipCard
                var card = new MembershipCard
                {
                    UserId = meta.UserId,
                    TierId = meta.TierId,
                    TicketId = ticket.TicketId,
                    SlotId = meta.SlotId,
                    StartTime = currentDbTime,
                    EndTime = currentDbTime.AddMonths(meta.DurationMonths),
                    Status = ParkingStatuses.MonthlyCardActive, // "Active"
                    IsDeleted = false
                };
                await _context.MembershipCards.AddAsync(card);
                await _context.SaveChangesAsync(); // Lưu để lấy MembershipCardId

                // 6. Lưu các biển số xe đăng ký
                foreach (var plate in meta.LicenseVehicles)
                {
                    var vehicle = new MembershipVehicle
                    {
                        MembershipCardId = card.MembershipCardId,
                        LicenseVehicle = plate,
                        IsActive = true
                    };
                    await _context.MembershipVehicles.AddAsync(vehicle);
                }

                // 7. Cập nhật Invoice sang SUCCESS
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = currentDbTime;
                invoice.UpdatedDate = currentDbTime;

                // 8. Giải phóng cache
                _cache.Remove(txnRef);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Đăng ký thành công thẻ thành viên: Tài xế ID {UserId}, Thẻ ID {CardId}, Hạn dùng: {End}",
                    meta.UserId, card.MembershipCardId, card.EndTime);

                return new PaymentResultDto { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi nghiêm trọng khi xác nhận thanh toán thẻ thành viên cho giao dịch {TxnRef}", txnRef);
                throw;
            }
        }

        public async Task<object?> GetMyActiveCardAsync(int userId)
        {
            var card = await _context.MembershipCards
                .Include(c => c.Ticket)
                .Include(c => c.Tier)
                .Include(c => c.MembershipVehicles)
                .Where(c => c.UserId == userId && c.Status == "Active" && !c.IsDeleted)
                .OrderByDescending(c => c.EndTime)
                .Select(c => new {
                    c.MembershipCardId,
                    c.StartTime,
                    c.EndTime,
                    c.Status,
                    c.SlotId,
                    Tier = new { c.Tier.TierId, c.Tier.TierName, c.Tier.Price, c.Tier.DurationMonths },
                    TicketCode = c.Ticket != null ? c.Ticket.TicketCode : null,
                    Vehicles = c.MembershipVehicles.Where(v => v.IsActive).Select(v => v.LicenseVehicle).ToList()
                })
                .FirstOrDefaultAsync();

            return card;
        }

        public async Task<List<object>> GetActiveTiersAsync()
        {
            return await _context.MembershipTiers
                .Include(t => t.Type)
                .Where(t => !t.IsDeleted)
                .Select(t => new {
                    t.TierId,
                    t.TierName,
                    t.DurationMonths,
                    t.MaxVehicles,
                    t.TypeId,
                    t.Price,
                    VehicleType = t.Type != null ? t.Type.TypeName : "Unknown"
                })
                .Cast<object>()
                .ToListAsync();
        }

        public class MembershipRegistrationMetadata
        {
            public int UserId { get; set; }
            public int TierId { get; set; }
            public int SlotId { get; set; }
            public int DurationMonths { get; set; }
            public string TicketCode { get; set; } = null!;
            public List<string> LicenseVehicles { get; set; } = new List<string>();
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
    }
}
