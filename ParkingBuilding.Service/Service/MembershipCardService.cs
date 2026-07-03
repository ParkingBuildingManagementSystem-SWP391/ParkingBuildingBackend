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
using System.Data;
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
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                // 1. Xác thực tài xế tồn tại
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted)
                           ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài xế.");

                // 2. Xác thực gói cước thành viên (Tier)
                var tier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == dto.TierId && !t.IsDeleted)
                             ?? throw new KeyNotFoundException("Gói cước thành viên không tồn tại hoặc đã bị xóa.");

                // 3. Kiểm tra tính hợp lệ của DTO so với Tier
                var hasSameVehicleTypePackage = await _context.MembershipCards
                    .Include(c => c.Tier)
                    .AnyAsync(c => c.UserId == userId
                                && c.Tier.TypeId == tier.TypeId
                                && (c.Status == ParkingStatuses.MonthlyCardActive
                                    || c.Status == ParkingStatuses.MonthlyCardPendingPayment)
                                && !c.IsDeleted);
                if (hasSameVehicleTypePackage)
                {
                    throw new InvalidOperationException("Tai xe da co goi thanh vien cho loai xe nay dang hoat dong hoac dang cho thanh toan.");
                }

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

                // 5. Xác thực và khóa ô đỗ xe – mỗi gói thành viên luôn giữ đúng 1 slot
                if (cleanPlates.Distinct().Count() != cleanPlates.Count)
                    throw new ArgumentException("Danh sĂ¡ch biá»ƒn sá»‘ xe khĂ´ng Ä‘Æ°á»£c chá»©a biá»ƒn sá»‘ trĂ¹ng láº·p.");

                var registeredPlates = await GetRegisteredMembershipPlatesAsync(cleanPlates);
                if (registeredPlates.Count > 0)
                {
                    throw new ArgumentException($"Bien so {string.Join(", ", registeredPlates)} da duoc dang ky trong goi thanh vien khac.");
                }

                var targetSlotIds = dto.SlotIds ?? new List<int>();
                if (targetSlotIds.Count == 0 && dto.SlotId.HasValue)
                    targetSlotIds.Add(dto.SlotId.Value);

                if (targetSlotIds.Distinct().Count() != targetSlotIds.Count)
                    throw new ArgumentException("Danh sách ô đỗ xe không được chứa ô đỗ trùng lặp.");

                if (targetSlotIds.Count != 1)
                    throw new ArgumentException("Vui lòng chọn đúng 1 ô đỗ cố định.");

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

                // 7. Sinh 1 TicketCode duy nhất
                string singleTicketCode = $"MBC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

                // 8. Tạo mã giao dịch
                string txnRef = $"MBC_{targetSlotIds[0]}_{DateTime.UtcNow.Ticks}";

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

                    // WALLET: 1 Ticket + 1 Card + 1 MembershipSlot + N Vehicles
                    var ticket = new Ticket
                    {
                        TicketCode = singleTicketCode,
                        TicketStatus = ParkingStatuses.TicketActive
                    };
                    await _context.Tickets.AddAsync(ticket);
                    await _context.SaveChangesAsync();

                    var card = new MembershipCard
                    {
                        UserId = userId,
                        TierId = tier.TierId,
                        TicketId = ticket.TicketId,
                        StartTime = startTime,
                        EndTime = endTime,
                        Status = ParkingStatuses.MonthlyCardActive,
                        IsDeleted = false
                    };
                    await _context.MembershipCards.AddAsync(card);
                    await _context.SaveChangesAsync();

                    // Gắn slot vào card
                    await _context.MembershipSlots.AddAsync(new MembershipSlot
                    {
                        MembershipCardId = card.MembershipCardId,
                        SlotId = slots[0].SlotId
                    });

                    // Gắn biển số xe
                    foreach (var plate in cleanPlates)
                    {
                        await _context.MembershipVehicles.AddAsync(new MembershipVehicle
                        {
                            MembershipCardId = card.MembershipCardId,
                            LicenseVehicle = plate,
                            IsActive = true
                        });
                    }


                    await _context.SaveChangesAsync();

                    // Invoice SUCCESS
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
                        TicketCode = singleTicketCode,
                        TicketCodes = new List<string> { singleTicketCode },
                        AmountToPay = amountToPay,
                        SlotId = targetSlotIds[0],
                        SlotIds = targetSlotIds,
                        SlotNames = slots.Select(s => s.SlotName).ToList(),
                        LicenseVehicles = cleanPlates,
                        StartTime = startTime,
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

                    // VNPAY: 1 Ticket TEMP + 1 Card PendingPayment + 1 MembershipSlot + N Vehicles
                    var tempTicketCode = $"TEMP_{txnRef}_{singleTicketCode}";
                    var pendingTicket = new Ticket
                    {
                        TicketCode = tempTicketCode,
                        TicketStatus = ParkingStatuses.MonthlyCardPendingPayment
                    };
                    await _context.Tickets.AddAsync(pendingTicket);
                    await _context.SaveChangesAsync();

                    var pendingCard = new MembershipCard
                    {
                        UserId = userId,
                        TierId = tier.TierId,
                        TicketId = pendingTicket.TicketId,
                        StartTime = startTime,
                        EndTime = endTime,
                        Status = ParkingStatuses.MonthlyCardPendingPayment,
                        IsDeleted = false
                    };
                    await _context.MembershipCards.AddAsync(pendingCard);
                    await _context.SaveChangesAsync();

                    await _context.MembershipSlots.AddAsync(new MembershipSlot
                    {
                        MembershipCardId = pendingCard.MembershipCardId,
                        SlotId = slots[0].SlotId
                    });

                    foreach (var plate in cleanPlates)
                    {
                        await _context.MembershipVehicles.AddAsync(new MembershipVehicle
                        {
                            MembershipCardId = pendingCard.MembershipCardId,
                            LicenseVehicle = plate,
                            IsActive = false
                        });
                    }
                    await _context.SaveChangesAsync();

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
                        TicketCode = singleTicketCode,
                        TicketCodes = new List<string> { singleTicketCode },
                        AmountToPay = amountToPay,
                        SlotId = targetSlotIds[0],
                        SlotIds = targetSlotIds,
                        SlotNames = slots.Select(s => s.SlotName).ToList(),
                        LicenseVehicles = cleanPlates,
                        StartTime = startTime,
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
                        ticket.TicketStatus = ParkingStatuses.TicketExpired;
                        if (ticket.MembershipCard != null)
                        {
                            ticket.MembershipCard.Status = ParkingStatuses.MonthlyCardExpired;

                            // Giải phóng slot từ MembershipSlots
                            var membershipSlots = await _context.MembershipSlots
                                .Where(ms => ms.MembershipCardId == ticket.MembershipCard.MembershipCardId)
                                .ToListAsync();
                            foreach (var ms in membershipSlots)
                            {
                                var slot = await _context.ParkingSlots.FindAsync(ms.SlotId);
                                if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                                {
                                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                    _context.ParkingSlots.Update(slot);
                                }
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
                .Include(c => c.MembershipSlots)
                    .ThenInclude(ms => ms.Slot)
                .Where(c => c.UserId == userId && c.Status == "Active" && !c.IsDeleted)
                .OrderByDescending(c => c.EndTime)
                .Select(c => new {
                    c.MembershipCardId,
                    c.StartTime,
                    c.EndTime,
                    c.Status,
                    Tier = new { c.Tier.TierId, c.Tier.TierName, c.Tier.Price, c.Tier.DurationMonths },
                    TicketCode = c.Ticket != null ? c.Ticket.TicketCode : null,
                    Vehicles = c.MembershipVehicles.Where(v => v.IsActive).Select(v => v.LicenseVehicle).ToList(),
                    Slots = c.MembershipSlots.Select(ms => new { ms.SlotId, ms.Slot.SlotName }).ToList()
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

        public async Task<bool> CancelMembershipCardAsync(int cardId, int userId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var card = await _context.MembershipCards
                    .Include(c => c.Ticket)
                    .Include(c => c.MembershipVehicles)
                    .Include(c => c.MembershipSlots)
                    .FirstOrDefaultAsync(c => c.MembershipCardId == cardId
                                           && c.UserId == userId
                                           && (c.Status == ParkingStatuses.MonthlyCardActive || c.Status == ParkingStatuses.MonthlyCardPendingPayment)
                                           && !c.IsDeleted);
                if (card == null) return false;

                card.Status = ParkingStatuses.MonthlyCardCancelled;
                card.IsDeleted = true;
                card.Ticket.TicketStatus = ParkingStatuses.TicketExpired;

                foreach (var vehicle in card.MembershipVehicles)
                {
                    vehicle.IsActive = false;
                }

                foreach (var ms in card.MembershipSlots)
                {
                    var slot = await _context.ParkingSlots.FindAsync(ms.SlotId);
                    if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                    {
                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                        _context.ParkingSlots.Update(slot);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                _logger.LogInformation("Đã hủy MembershipCard {CardId} của User {UserId}.", cardId, userId);
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UpdateMembershipVehiclesAsync(int cardId, int userId, List<string> newPlates)
        {
            if (newPlates == null || newPlates.Count == 0)
                throw new ArgumentException("Vui lĂ²ng cung cáº¥p Ă­t nháº¥t má»™t biá»ƒn sá»‘ xe.");

            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

            var card = await _context.MembershipCards
                .Include(c => c.Tier)
                .Include(c => c.MembershipVehicles)
                .FirstOrDefaultAsync(c => c.MembershipCardId == cardId
                                       && c.UserId == userId
                                       && c.Status == ParkingStatuses.MonthlyCardActive
                                       && !c.IsDeleted);
            if (card == null) return false;

            var cleanPlates = new List<string>();
            foreach (var plate in newPlates)
            {
                if (string.IsNullOrWhiteSpace(plate))
                    throw new ArgumentException("Biá»ƒn sá»‘ xe khĂ´ng Ä‘Æ°á»£c Ä‘á»ƒ trá»‘ng.");

                var cleanPlate = plate.Trim().ToUpper();
                if (card.Tier.TypeId != 1)
                {
                    if (!LicensePlateHelper.IsValidLicensePlate(cleanPlate, out var validatedPlate))
                        throw new ArgumentException($"Biá»ƒn sá»‘ xe '{cleanPlate}' khĂ´ng há»£p lá»‡: {LicensePlateHelper.GetErrorMessage()}");

                    cleanPlate = validatedPlate;
                }

                cleanPlates.Add(cleanPlate);
            }

            if (cleanPlates.Distinct().Count() != cleanPlates.Count)
                throw new ArgumentException("Danh sĂ¡ch biá»ƒn sá»‘ xe khĂ´ng Ä‘Æ°á»£c chá»©a biá»ƒn sá»‘ trĂ¹ng láº·p.");

            if (cleanPlates.Count > card.Tier.MaxVehicles)
                throw new ArgumentException($"Gói này chỉ cho phép tối đa {card.Tier.MaxVehicles} biển số.");

            var registeredPlates = await GetRegisteredMembershipPlatesAsync(cleanPlates, cardId);
            if (registeredPlates.Count > 0)
            {
                throw new ArgumentException($"Bien so {string.Join(", ", registeredPlates)} da duoc dang ky trong goi thanh vien khac.");
            }

            // Xóa cũ, thêm mới
            _context.MembershipVehicles.RemoveRange(card.MembershipVehicles);
            foreach (var plate in cleanPlates)
            {
                await _context.MembershipVehicles.AddAsync(new MembershipVehicle
                {
                    MembershipCardId = cardId,
                    LicenseVehicle = plate,
                    IsActive = true
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        private async Task<List<string>> GetRegisteredMembershipPlatesAsync(List<string> cleanPlates, int? excludedCardId = null)
        {
            var query = _context.MembershipVehicles
                .Include(v => v.MembershipCard)
                .Where(v => cleanPlates.Contains(v.LicenseVehicle)
                            && !v.MembershipCard.IsDeleted
                            && (v.MembershipCard.Status == ParkingStatuses.MonthlyCardActive
                                || v.MembershipCard.Status == ParkingStatuses.MonthlyCardPendingPayment));

            if (excludedCardId.HasValue)
            {
                query = query.Where(v => v.MembershipCardId != excludedCardId.Value);
            }

            return await query
                .Select(v => v.LicenseVehicle)
                .Distinct()
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
