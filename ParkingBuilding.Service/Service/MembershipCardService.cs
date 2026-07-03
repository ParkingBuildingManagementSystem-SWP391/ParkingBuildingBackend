using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class MembershipCardService : IMembershipCardService
    {
        private static readonly ConcurrentDictionary<string, MembershipRegistrationMetadata> _pendingRegistrations = 
            new ConcurrentDictionary<string, MembershipRegistrationMetadata>();

        private readonly ParkingManagementDbContext _context;
        private readonly IVnPayService _vnPayService;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<MembershipCardService> _logger;
        private readonly IMemoryCache _cache;
        private readonly IWalletService _walletService;
        private readonly IEmailService _emailService;

        public MembershipCardService(
            ParkingManagementDbContext context,
            IVnPayService vnPayService,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<MembershipCardService> logger,
            IMemoryCache cache,
            IWalletService walletService,
            IEmailService emailService)
        {
            _context = context;
            _vnPayService = vnPayService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _cache = cache;
            _walletService = walletService;
            _emailService = emailService;
        }


        public async Task<MembershipCardRegistrationResponseDto> RegisterMembershipCardAsync(int userId, RegisterMembershipCardDto dto, string ipAddress)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted)
                       ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài xế.");

            var tier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == dto.TierId && !t.IsDeleted)
                         ?? throw new KeyNotFoundException("Gói cước thành viên không tồn tại hoặc đã bị xóa.");

            var hasSameVehicleTypePackage = await _context.MembershipCards
                .Include(c => c.Tier)
                .AnyAsync(c => c.UserId == userId
                            && c.Tier.TypeId == tier.TypeId
                            && c.Status == ParkingStatuses.MonthlyCardActive
                            && !c.IsDeleted);
            if (hasSameVehicleTypePackage)
            {
                throw new InvalidOperationException("Tài xế đã có gói thành viên hoạt động cho loại xe này.");
            }

            var now = DateTime.UtcNow;
            foreach (var kvp in _pendingRegistrations)
            {
                if (now - kvp.Value.CreatedTime > TimeSpan.FromMinutes(15))
                    continue;
                if (kvp.Value.UserId == userId)
                {
                    var cachedTier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == kvp.Value.TierId);
                    if (cachedTier != null && cachedTier.TypeId == tier.TypeId)
                    {
                        throw new InvalidOperationException("Tài xế đang có giao dịch đăng ký chờ thanh toán cho loại xe này.");
                    }
                }
            }

            if (tier.DurationMonths != 1 && tier.DurationMonths != 6 && tier.DurationMonths != 12)
            {
                throw new ArgumentException("Thời hạn thuê của gói thành viên không hợp lệ (chỉ được phép là 1, 6 hoặc 12 tháng).");
            }

            if (dto.LicenseVehicles == null || dto.LicenseVehicles.Count == 0)
            {
                throw new ArgumentException("Vui lòng cung cấp ít nhất một biển số xe.");
            }

            if (tier.MaxVehicles == 1)
            {
                if (dto.LicenseVehicles.Count != 1)
                {
                    throw new ArgumentException("Gói thành viên này chỉ cho phép đăng ký duy nhất 1 biển số xe.");
                }
            }
            else
            {
                if (dto.LicenseVehicles.Count > tier.MaxVehicles)
                {
                    throw new ArgumentException($"Gói thành viên này chỉ cho phép đăng ký tối đa {tier.MaxVehicles} biển số xe.");
                }
            }

            var cleanPlates = new List<string>();
            foreach (var plate in dto.LicenseVehicles)
            {
                if (string.IsNullOrWhiteSpace(plate))
                {
                    throw new ArgumentException("Biển số xe không được để trống.");
                }

                string cleanPlate = plate.Trim().ToUpper();
                if (tier.TypeId != 1)
                {
                    if (!LicensePlateHelper.IsValidLicensePlate(cleanPlate, out string validatedPlate))
                    {
                        throw new ArgumentException($"Biển số xe '{cleanPlate}' không hợp lệ: {LicensePlateHelper.GetErrorMessage()}");
                    }
                    cleanPlate = validatedPlate;
                }
                cleanPlates.Add(cleanPlate);
            }

            if (cleanPlates.Distinct().Count() != cleanPlates.Count)
                throw new ArgumentException("Danh sách biển số xe không được chứa biển số trùng lặp.");

            var registeredPlates = await GetRegisteredMembershipPlatesAsync(cleanPlates);

            var expiredKeys = new List<string>();
            var registeredInCache = new List<string>();
            foreach (var kvp in _pendingRegistrations)
            {
                if (now - kvp.Value.CreatedTime > TimeSpan.FromMinutes(15))
                {
                    expiredKeys.Add(kvp.Key);
                    continue;
                }
                foreach (var plate in kvp.Value.LicenseVehicles)
                {
                    if (cleanPlates.Contains(plate))
                    {
                        registeredInCache.Add(plate);
                    }
                }
            }

            foreach (var key in expiredKeys)
            {
                _pendingRegistrations.TryRemove(key, out _);
            }

            if (registeredPlates.Count > 0 || registeredInCache.Count > 0)
            {
                var allDuplicated = registeredPlates.Concat(registeredInCache).Distinct();
                throw new ArgumentException($"Biển số {string.Join(", ", allDuplicated)} đã được đăng ký trong gói thành viên khác.");
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

            var slot = slots[0];
            if (slot.SlotStatus != ParkingStatuses.SlotAvailable)
            {
                throw new ArgumentException($"Ô đỗ {slot.SlotName} hiện tại không trống.");
            }
            if (slot.TypeId != tier.TypeId)
            {
                throw new ArgumentException($"Ô đỗ {slot.SlotName} không khớp với loại xe của gói thành viên.");
            }

            decimal amountToPay = tier.Price;
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var startTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
            var endTime = startTime.AddMonths(tier.DurationMonths);

            string singleTicketCode = $"MBC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
            string txnRef = $"MBC_{slot.SlotId}_{DateTime.UtcNow.Ticks}";

            if (dto.PaymentMethod != null && dto.PaymentMethod.ToUpper() == "AUTO")
            {
                var walletBalance = await _walletService.GetBalanceAsync(userId);
                dto.PaymentMethod = walletBalance >= amountToPay ? "WALLET" : "VNPAY";
            }

            if (dto.PaymentMethod != null && dto.PaymentMethod.ToUpper() == "WALLET")
            {
                using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                try
                {
                    var dbSlot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slot.SlotId && !s.IsDeleted);
                    if (dbSlot == null || dbSlot.SlotStatus != ParkingStatuses.SlotAvailable)
                    {
                        throw new ArgumentException($"Ô đỗ {slot.SlotName} hiện tại không trống.");
                    }

                    bool paymentSuccess = await _walletService.ProcessWalletPaymentAsync(
                        userId,
                        amountToPay,
                        $"Thanh toán gói {tier.TierName} (Slot: {slot.SlotName})"
                    );

                    if (!paymentSuccess)
                    {
                        throw new InvalidOperationException("Số dư ví không đủ để thanh toán gói thành viên.");
                    }

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
                        IsDeleted = false,
                        SlotId = slot.SlotId
                    };
                    await _context.MembershipCards.AddAsync(card);
                    await _context.SaveChangesAsync();

                    await _context.MembershipSlots.AddAsync(new MembershipSlot
                    {
                        MembershipCardId = card.MembershipCardId,
                        SlotId = slot.SlotId
                    });

                    foreach (var plate in cleanPlates)
                    {
                        await _context.MembershipVehicles.AddAsync(new MembershipVehicle
                        {
                            MembershipCardId = card.MembershipCardId,
                            LicenseVehicle = plate,
                            IsActive = true
                        });
                    }

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

                    dbSlot.SlotStatus = ParkingStatuses.SlotReserved;
                    _context.ParkingSlots.Update(dbSlot);

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    try
                    {
                        if (!string.IsNullOrEmpty(user.Email))
                        {
                            string subject = "Xác nhận đăng ký thẻ thành viên thành công - Smart Parking Building";
                            string licensePlatesStr = string.Join(", ", cleanPlates);
                            string body = $@"
                                <h3>Đăng ký thẻ thành viên thành công</h3>
                                <p>Chào <b>{user.Username}</b>,</p>
                                <p>Bạn đã thanh toán thành công qua Ví và kích hoạt thẻ thành viên tại Smart Parking Building.</p>
                                <ul>
                                    <li><b>Gói cước:</b> {tier.TierName} ({tier.DurationMonths} tháng)</li>
                                    <li><b>Mã vé (Ticket Code):</b> {singleTicketCode}</li>
                                    <li><b>Thời gian hiệu lực:</b> {startTime:dd/MM/yyyy HH:mm} đến {endTime:dd/MM/yyyy HH:mm}</li>
                                    <li><b>Danh sách biển số đăng ký:</b> {licensePlatesStr}</li>
                                    <li><b>Vị trí ô đỗ cố định:</b> {slot.SlotName}</li>
                                </ul>
                                <br/>
                                <p>Trân trọng,<br/>Ban quản lý Smart Parking Building</p>";

                            await _emailService.SendEmailAsync(user.Email, subject, body);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Lỗi gửi email xác nhận đăng ký thẻ thành viên qua ví cho User ID {UserId}", userId);
                    }

                    return new MembershipCardRegistrationResponseDto
                    {
                        Username = user.Username,
                        TicketCode = singleTicketCode,
                        TicketCodes = new List<string> { singleTicketCode },
                        AmountToPay = amountToPay,
                        SlotId = slot.SlotId,
                        SlotIds = new List<int> { slot.SlotId },
                        SlotNames = new List<string> { slot.SlotName },
                        LicenseVehicles = cleanPlates,
                        StartTime = startTime,
                        EndTime = endTime,
                        PaymentUrl = null,
                        InvoiceId = invoice.InvoiceId,
                        PaymentStatus = invoice.PaymentStatus,
                        PaymentMethod = invoice.PaymentMethod,
                        TransactionCode = invoice.TransactionCode
                    };
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
            else
            {
                var metadata = new MembershipRegistrationMetadata
                {
                    UserId = userId,
                    TierId = tier.TierId,
                    SlotIds = new List<int> { slot.SlotId },
                    DurationMonths = tier.DurationMonths,
                    TicketCodes = new List<string> { singleTicketCode },
                    LicenseVehicles = cleanPlates,
                    StartTime = startTime,
                    EndTime = endTime,
                    CreatedTime = DateTime.UtcNow
                };

                _pendingRegistrations[txnRef] = metadata;

                var pendingInvoice = new Invoice
                {
                    SessionId = null,
                    TotalAmount = amountToPay,
                    PaymentMethod = "VNPAY",
                    PaymentStatus = "PENDING",
                    TransactionCode = txnRef,
                    CreatedDate = DateTime.UtcNow,
                    PaymentTime = null,
                    UpdatedDate = null
                };
                await _context.Invoices.AddAsync(pendingInvoice);
                await _context.SaveChangesAsync();

                string paymentUrl = _vnPayService.CreatePaymentUrl(
                    txnRef: txnRef,
                    amount: amountToPay,
                    orderInfo: $"DK the thanh vien goi {tier.TierName} cho {tier.DurationMonths} thang",
                    returnUrl: _vnPayConfig.ReturnUrl + "?type=membership&txnRef=" + txnRef,
                    ipAddress: ipAddress
                );

                return new MembershipCardRegistrationResponseDto
                {
                    Username = user.Username,
                    TicketCode = singleTicketCode,
                    TicketCodes = new List<string> { singleTicketCode },
                    AmountToPay = amountToPay,
                    SlotId = slot.SlotId,
                    SlotIds = new List<int> { slot.SlotId },
                    SlotNames = new List<string> { slot.SlotName },
                    LicenseVehicles = cleanPlates,
                    StartTime = startTime,
                    EndTime = endTime,
                    PaymentUrl = paymentUrl,
                    InvoiceId = pendingInvoice.InvoiceId,
                    PaymentStatus = pendingInvoice.PaymentStatus,
                    PaymentMethod = pendingInvoice.PaymentMethod,
                    TransactionCode = pendingInvoice.TransactionCode
                };
            }
        }

        public async Task<PaymentResultDto> ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            if (!_pendingRegistrations.TryGetValue(txnRef, out var metadata))
            {
                return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Yêu cầu đăng ký đã hết hạn hoặc không tồn tại" };
            }

            if (responseCode != "00" || transactionStatus != "00")
            {
                var failedInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (failedInvoice != null && failedInvoice.PaymentStatus == "PENDING")
                {
                    failedInvoice.PaymentStatus = "FAILED";
                    failedInvoice.UpdatedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                _pendingRegistrations.TryRemove(txnRef, out _);
                return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ cổng VNPay" };
            }

            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == metadata.UserId && !u.IsDeleted);
                var tier = await _context.MembershipTiers.FirstOrDefaultAsync(t => t.TierId == metadata.TierId && !t.IsDeleted);
                if (user == null || tier == null)
                {
                    throw new Exception("Thông tin người dùng hoặc gói thành viên không hợp lệ.");
                }

                if (amount != tier.Price)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "So tien thanh toan khong khop voi gia goi thanh vien." };
                }

                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    invoice = new Invoice
                    {
                        SessionId = null,
                        TotalAmount = amount,
                        PaymentMethod = "VNPAY",
                        PaymentStatus = "PENDING",
                        TransactionCode = txnRef,
                        CreatedDate = DateTime.UtcNow
                    };
                    await _context.Invoices.AddAsync(invoice);
                    await _context.SaveChangesAsync();
                }

                if (invoice.PaymentStatus == "SUCCESS")
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hoa don da duoc xu ly truoc do.", InvoiceId = invoice.InvoiceId };
                }

                if (invoice.PaymentStatus != "PENDING")
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hoa don khong con o trang thai cho thanh toan.", InvoiceId = invoice.InvoiceId };
                }

                var hasSameVehicleTypePackage = await _context.MembershipCards
                    .Include(c => c.Tier)
                    .AnyAsync(c => c.UserId == metadata.UserId
                                && c.Tier.TypeId == tier.TypeId
                                && c.Status == ParkingStatuses.MonthlyCardActive
                                && !c.IsDeleted);
                if (hasSameVehicleTypePackage)
                {
                    throw new InvalidOperationException("Tài xế đã có gói thành viên hoạt động cho loại xe này.");
                }

                var slotId = metadata.SlotIds.First();
                var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId && !s.IsDeleted);
                if (slot == null || slot.SlotStatus != ParkingStatuses.SlotAvailable)
                {
                    throw new Exception($"Ô đỗ {slot?.SlotName ?? slotId.ToString()} hiện không trống hoặc không tồn tại.");
                }

                var registeredPlates = await GetRegisteredMembershipPlatesAsync(metadata.LicenseVehicles);
                if (registeredPlates.Count > 0)
                {
                    throw new Exception($"Biển số {string.Join(", ", registeredPlates)} đã được đăng ký trong gói thành viên khác.");
                }

                var ticket = new Ticket
                {
                    TicketCode = metadata.TicketCodes.First(),
                    TicketStatus = ParkingStatuses.TicketActive
                };
                await _context.Tickets.AddAsync(ticket);
                await _context.SaveChangesAsync();

                var card = new MembershipCard
                {
                    UserId = metadata.UserId,
                    TierId = metadata.TierId,
                    TicketId = ticket.TicketId,
                    StartTime = metadata.StartTime,
                    EndTime = metadata.EndTime,
                    Status = ParkingStatuses.MonthlyCardActive,
                    IsDeleted = false,
                    SlotId = slot.SlotId
                };
                await _context.MembershipCards.AddAsync(card);
                await _context.SaveChangesAsync();

                await _context.MembershipSlots.AddAsync(new MembershipSlot
                {
                    MembershipCardId = card.MembershipCardId,
                    SlotId = slot.SlotId
                });

                foreach (var plate in metadata.LicenseVehicles)
                {
                    await _context.MembershipVehicles.AddAsync(new MembershipVehicle
                    {
                        MembershipCardId = card.MembershipCardId,
                        LicenseVehicle = plate,
                        IsActive = true
                    });
                }

                invoice.TotalAmount = amount;
                invoice.PaymentMethod = "VNPAY";
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;
                _context.Invoices.Update(invoice);

                slot.SlotStatus = ParkingStatuses.SlotReserved;
                _context.ParkingSlots.Update(slot);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _pendingRegistrations.TryRemove(txnRef, out _);

                _logger.LogInformation("Xác nhận thanh toán thành công qua VNPay cho giao dịch {TxnRef}. Đã kích hoạt các thẻ thành viên.", txnRef);

                try
                {
                    if (!string.IsNullOrEmpty(user.Email))
                    {
                        string subject = "Xác nhận đăng ký thẻ thành viên thành công - Smart Parking Building";
                        string licensePlatesStr = string.Join(", ", metadata.LicenseVehicles);
                        string body = $@"
                            <h3>Đăng ký thẻ thành viên thành công</h3>
                            <p>Chào <b>{user.Username}</b>,</p>
                            <p>Bạn đã thanh toán thành công qua VNPay và kích hoạt thẻ thành viên tại Smart Parking Building.</p>
                            <ul>
                                <li><b>Gói cước:</b> {tier.TierName} ({tier.DurationMonths} tháng)</li>
                                <li><b>Mã vé (Ticket Code):</b> {ticket.TicketCode}</li>
                                <li><b>Thời gian hiệu lực:</b> {metadata.StartTime:dd/MM/yyyy HH:mm} đến {metadata.EndTime:dd/MM/yyyy HH:mm}</li>
                                <li><b>Danh sách biển số đăng ký:</b> {licensePlatesStr}</li>
                                <li><b>Vị trí ô đỗ cố định:</b> {slot.SlotName}</li>
                            </ul>
                            <br/>
                            <p>Trân trọng,<br/>Ban quản lý Smart Parking Building</p>";

                        await _emailService.SendEmailAsync(user.Email, subject, body);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi email xác nhận đăng ký thẻ thành viên qua VNPay cho User ID {UserId}", metadata.UserId);
                }

                return new PaymentResultDto
                {
                    Success = true,
                    InvoiceId = invoice.InvoiceId,
                    Message = "Dang ky goi thanh vien va thanh toan VNPay thanh cong."
                };
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

                var hasActiveSession = await _context.ParkingSessions
                    .AnyAsync(s => s.TicketId == card.TicketId
                                && s.SessionStatus == ParkingStatuses.SessionInProgress
                                && !s.IsDeleted);

                if (hasActiveSession)
                {
                    throw new InvalidOperationException("Thẻ đang có xe trong bãi, vui lòng checkout xe trước khi hủy thẻ.");
                }

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

            if (card.Tier.MaxVehicles == 1)
            {
                if (cleanPlates.Count != 1)
                {
                    throw new ArgumentException("Gói thành viên này chỉ cho phép đăng ký duy nhất 1 biển số xe.");
                }
            }
            else
            {
                if (cleanPlates.Count > card.Tier.MaxVehicles)
                {
                    throw new ArgumentException($"Gói này chỉ cho phép tối đa {card.Tier.MaxVehicles} biển số.");
                }
            }

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
                            && v.MembershipCard.Status == ParkingStatuses.MonthlyCardActive);

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
            public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
        }
    }
}
