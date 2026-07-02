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
        private readonly IWalletService _walletService;

        public MembershipCardService(
            ParkingManagementDbContext context,
            IVnPayService vnPayService,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<MembershipCardService> logger,
            IMemoryCache cache,
            IWalletService walletService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _cache = cache;
            _walletService = walletService;
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

                // 5. Xác thực và khóa ô đỗ xe (Slot) theo quy tắc Option A
                var targetSlotIds = dto.SlotIds ?? new List<int>();
                if (targetSlotIds.Count == 0 && dto.SlotId.HasValue)
                {
                    targetSlotIds.Add(dto.SlotId.Value);
                }

                // Quy tắc: 1 tháng = 1 slot, 6 tháng = 2 slots, 12 tháng = 3 slots
                if (targetSlotIds.Distinct().Count() != targetSlotIds.Count)
                {
                    throw new ArgumentException("Danh sách ô đỗ xe không được chứa ô đỗ trùng lặp.");
                }

                int expectedSlotsCount = tier.DurationMonths switch
                {
                    1 => 1,
                    6 => 2,
                    12 => 3,
                    _ => throw new ArgumentException("Thời hạn gói thành viên không hợp lệ.")
                };

                if (targetSlotIds.Count != expectedSlotsCount)
                {
                    throw new ArgumentException($"Gói cước {tier.TierName} ({tier.DurationMonths} tháng) yêu cầu chọn chính xác {expectedSlotsCount} ô đỗ cố định.");
                }

                var slots = await _context.ParkingSlots
                    .Where(s => targetSlotIds.Contains(s.SlotId) && !s.IsDeleted)
                    .ToListAsync();

                if (slots.Count != targetSlotIds.Count)
                {
                    throw new KeyNotFoundException("Một hoặc nhiều ô đỗ xe được chọn không tồn tại.");
                }

                foreach (var s in slots)
                {
                    if (s.SlotStatus != ParkingStatuses.SlotAvailable)
                    {
                        throw new ArgumentException($"Ô đỗ {s.SlotName} hiện tại không trống.");
                    }
                    if (s.TypeId != tier.TypeId)
                    {
                        throw new ArgumentException($"Ô đỗ {s.SlotName} không khớp với loại xe của gói thành viên.");
                    }

                    // Khóa tạm thời
                    s.SlotStatus = ParkingStatuses.SlotReserved;
                    _context.ParkingSlots.Update(s);
                }

                // 6. Tính toán số tiền thanh toán và thời hạn sử dụng
                decimal amountToPay = tier.Price;
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var startTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                var endTime = startTime.AddMonths(tier.DurationMonths);

                // 7. Tạo mã vé cho từng Membership Card tương ứng với mỗi slot
                var ticketCodes = new List<string>();
                for (int i = 0; i < slots.Count; i++)
                {
                    ticketCodes.Add($"MBC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}");
                }

                // 8. Tạo mã giao dịch chứa các SlotId để rollback khi thất bại
                string slotIdsString = string.Join("-", targetSlotIds);
                string txnRef = $"MBC_{slotIdsString}_{DateTime.UtcNow.Ticks}";

                if (dto.PaymentMethod != null && dto.PaymentMethod.ToUpper() == "WALLET")
                {
                    // A. XỬ LÝ THANH TOÁN BẰNG VÍ ĐIỆN TỬ
                    bool paymentSuccess = await _walletService.ProcessWalletPaymentAsync(
                        userId,
                        amountToPay,
                        $"Thanh toán gói {tier.TierName} (Slots: {string.Join(", ", slots.Select(s => s.SlotName))})"
                    );

                    if (!paymentSuccess)
                    {
                        throw new InvalidOperationException("Số dư ví không đủ để thanh toán gói thành viên.");
                    }

                    for (int i = 0; i < slots.Count; i++)
                    {
                        var targetSlot = slots[i];
                        var ticketCode = ticketCodes[i];

                        // Tạo Ticket hoạt động luôn
                        var ticket = new Ticket
                        {
                            TicketCode = ticketCode,
                            TicketStatus = ParkingStatuses.TicketActive
                        };
                        await _context.Tickets.AddAsync(ticket);
                        await _context.SaveChangesAsync();

                        // Tạo MembershipCard
                        var card = new MembershipCard
                        {
                            UserId = userId,
                            TierId = tier.TierId,
                            TicketId = ticket.TicketId,
                            SlotId = targetSlot.SlotId,
                            StartTime = startTime,
                            EndTime = endTime,
                            Status = ParkingStatuses.MonthlyCardActive,
                            IsDeleted = false
                        };
                        await _context.MembershipCards.AddAsync(card);
                        await _context.SaveChangesAsync();

                        // Đăng ký danh sách xe cho card này
                        foreach (var plate in cleanPlates)
                        {
                            var vehicle = new MembershipVehicle
                            {
                                MembershipCardId = card.MembershipCardId,
                                LicenseVehicle = plate,
                                IsActive = true
                            };
                            await _context.MembershipVehicles.AddAsync(vehicle);
                        }
                    }

                    // Tạo Invoice trạng thái SUCCESS
                    var invoice = new Invoice
                    {
                        SessionId = null,
                        TotalAmount = amountToPay,
                        PaymentMethod = "WALLET",
                        PaymentStatus = "SUCCESS",
                        TransactionCode = txnRef,
                        CreatedDate = startTime,
                        PaymentTime = startTime,
                        UpdatedDate = startTime
                    };
                    await _context.Invoices.AddAsync(invoice);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return new MembershipCardRegistrationResponseDto
                    {
                        Username = user.Username,
                        TicketCode = string.Join(", ", ticketCodes),
                        AmountToPay = amountToPay,
                        SlotId = targetSlotIds.FirstOrDefault(),
                        LicenseVehicles = cleanPlates,
                        EndTime = endTime,
                        PaymentUrl = null
                    };
                }
                else
                {
                    // B. LƯU TRỰC TIẾP VÀO DB VỚI TRẠNG THÁI PENDING (TRÁNH DÙNG RAM CACHE)
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

                    for (int i = 0; i < slots.Count; i++)
                    {
                        var targetSlot = slots[i];
                        var cleanTicketCode = ticketCodes[i];
                        var tempTicketCode = $"TEMP_{txnRef}_{cleanTicketCode}";

                        // Tạo Ticket trạng thái PendingPayment
                        var ticket = new Ticket
                        {
                            TicketCode = tempTicketCode,
                            TicketStatus = "PendingPayment"
                        };
                        await _context.Tickets.AddAsync(ticket);
                        await _context.SaveChangesAsync();

                        // Tạo MembershipCard trạng thái PendingPayment
                        var card = new MembershipCard
                        {
                            UserId = userId,
                            TierId = tier.TierId,
                            TicketId = ticket.TicketId,
                            SlotId = targetSlot.SlotId,
                            StartTime = startTime,
                            EndTime = endTime,
                            Status = "PendingPayment",
                            IsDeleted = false
                        };
                        await _context.MembershipCards.AddAsync(card);
                        await _context.SaveChangesAsync();

                        // Lưu danh sách xe ở trạng thái IsActive = false
                        foreach (var plate in cleanPlates)
                        {
                            var vehicle = new MembershipVehicle
                            {
                                MembershipCardId = card.MembershipCardId,
                                LicenseVehicle = plate,
                                IsActive = false
                            };
                            await _context.MembershipVehicles.AddAsync(vehicle);
                        }
                    }

                    // Tạo URL thanh toán VNPay
                    string paymentUrl = _vnPayService.CreatePaymentUrl(
                        txnRef: txnRef,
                        amount: amountToPay,
                        orderInfo: $"DK the thanh vien goi {tier.TierName} cho {tier.DurationMonths} thang",
                        returnUrl: _vnPayConfig.ReturnUrl + "?type=membership&invoiceId=" + invoice.InvoiceId,
                        ipAddress: ipAddress
                    );

                    await transaction.CommitAsync();

                    return new MembershipCardRegistrationResponseDto
                    {
                        Username = user.Username,
                        TicketCode = string.Join(", ", ticketCodes),
                        AmountToPay = amountToPay,
                        SlotId = targetSlotIds.FirstOrDefault(),
                        LicenseVehicles = cleanPlates,
                        EndTime = endTime,
                        PaymentUrl = paymentUrl
                    };
                }
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PaymentResultDto> ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
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

                // Lấy tiền tố TEMP_{txnRef}_
                var tempPrefix = $"TEMP_{txnRef}_";
                var tickets = await _context.Tickets
                    .Include(t => t.MembershipCard)
                        .ThenInclude(mc => mc!.MembershipVehicles)
                    .Where(t => t.TicketCode.StartsWith(tempPrefix))
                    .ToListAsync();

                // 2. Xử lý khi thanh toán thất bại
                if (responseCode != "00" || transactionStatus != "00")
                {
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;

                    foreach (var ticket in tickets)
                    {
                        ticket.TicketStatus = ParkingStatuses.TicketExpired; // "Expired"
                        if (ticket.MembershipCard != null)
                        {
                            ticket.MembershipCard.Status = ParkingStatuses.MonthlyCardExpired; // "Expired"
                            
                            var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == ticket.MembershipCard.SlotId);
                            if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                            {
                                slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                _context.ParkingSlots.Update(slot);
                            }
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ cổng VNPay" };
                }

                // 3. Xử lý thanh toán thành công
                if (tickets.Count == 0)
                {
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "03", Message = "Yêu cầu đăng ký đã hết hạn (quá 15 phút)" };
                }

                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var currentDbTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

                foreach (var ticket in tickets)
                {
                    // Trả lại TicketCode nguyên bản và đổi trạng thái hoạt động
                    var cleanCode = ticket.TicketCode.Replace(tempPrefix, "");
                    ticket.TicketCode = cleanCode;
                    ticket.TicketStatus = ParkingStatuses.TicketActive; // "Active"

                    if (ticket.MembershipCard != null)
                    {
                        var tier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == ticket.MembershipCard.TierId);
                        int durationMonths = tier?.DurationMonths ?? 1;

                        ticket.MembershipCard.Status = ParkingStatuses.MonthlyCardActive; // "Active"
                        ticket.MembershipCard.StartTime = currentDbTime;
                        ticket.MembershipCard.EndTime = currentDbTime.AddMonths(durationMonths);

                        foreach (var vehicle in ticket.MembershipCard.MembershipVehicles)
                        {
                            vehicle.IsActive = true;
                        }
                    }
                }

                // 4. Cập nhật hóa đơn
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = currentDbTime;
                invoice.UpdatedDate = currentDbTime;

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Xác nhận thanh toán thành công qua VNPay cho giao dịch {TxnRef}. Đã kích hoạt các thẻ thành viên.", txnRef);

                return new PaymentResultDto { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi nghiêm trọng khi xác nhận thanh toán thẻ thành viên cho giao dịch {TxnRef}", txnRef);
                throw;
            }
        }

        public async Task<List<object>> GetMyActiveCardsAsync(int userId)
        {
            var cards = await _context.MembershipCards
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
                .ToListAsync();

            return cards.Cast<object>().ToList();
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
            public List<int> SlotIds { get; set; } = new List<int>();
            public int DurationMonths { get; set; }
            public List<string> TicketCodes { get; set; } = new List<string>();
            public List<string> LicenseVehicles { get; set; } = new List<string>();
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
    }
}
