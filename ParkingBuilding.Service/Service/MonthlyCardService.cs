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
using System.Text.Json;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class MonthlyCardService : IMonthlyCardService
    {

        private readonly ParkingManagementDbContext _context;
        private readonly IVnPayService _vnPayService;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<MonthlyCardService> _logger;
        private readonly IMemoryCache _cache;

        public MonthlyCardService(
            ParkingManagementDbContext context,
            IVnPayService vnPayService,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<MonthlyCardService> logger,
            IMemoryCache cache)
        {
            _context = context;
            _vnPayService = vnPayService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _cache = cache;
        }

        public async Task<MonthlyCardRegistrationResponseDto> RegisterMonthlyCardAsync(int userId, RegisterMonthlyCardDto dto, string ipAddress)
        {
            // 1. Kiểm tra định dạng biển số xe (tận dụng LicensePlateHelper)
            string cleanLicense;
            if (dto.TariffId == 1) // Xe đạp
            {
                cleanLicense = dto.LicenseVehicle.Trim().ToUpper();
                if (!cleanLicense.StartsWith("BIKE_"))
                {
                    cleanLicense = $"BIKE_{cleanLicense}";
                }
            }
            else // Xe máy (2) và Xe hơi (3)
            {
                if (!LicensePlateHelper.IsValidLicensePlate(dto.LicenseVehicle, out string validatedPlate))
                {
                    throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
                }
                cleanLicense = validatedPlate;
            }

            // 2. Lấy thông tin User và gói cước (Tariff) tương ứng
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted)
                       ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài xế.");

            var tariff = await _context.MonthlyTariffs.FirstOrDefaultAsync(t => t.TariffId == dto.TariffId && !t.IsDeleted)
                         ?? throw new KeyNotFoundException("Gói cước thẻ tháng không tồn tại hoặc đã bị xóa.");

            // 2.1. Kiểm tra ô đỗ xe (SlotId)
            var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == dto.SlotId && !s.IsDeleted)
                       ?? throw new KeyNotFoundException("Chỗ đỗ xe không tồn tại hoặc đã bị xóa.");

            if (slot.TypeId != tariff.TypeId)
            {
                throw new ArgumentException("Chỗ đỗ xe đã chọn không phù hợp với loại xe của gói cước.");
            }

            var hasActiveMonthly = await _context.MonthlyCards.AnyAsync(mc => mc.SlotId == dto.SlotId && mc.Status == ParkingStatuses.MonthlyCardActive && !mc.IsDeleted);
            if (hasActiveMonthly)
            {
                throw new ArgumentException("Chỗ đỗ xe này đã được đăng ký bởi một thẻ tháng khác đang hoạt động.");
            }

            // 3. Tính toán số tiền phải trả và thời hạn
            decimal amountToPay = tariff.MonthlyPrice * dto.DurationMonths;

            // Định dạng múi giờ Việt Nam (SE Asia Standard Time) để đồng bộ lưu trữ
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var startTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
            var endTime = startTime.AddMonths(dto.DurationMonths);

            // 4. Tạo mã vé ngẫu nhiên cho thẻ tháng
            string ticketCode = $"MC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

            // 5. Tạo mã giao dịch VNPay
            string txnRef = "MCR_" + DateTime.UtcNow.Ticks;
            // 6. Đóng gói dữ liệu đăng ký vào Class cụ thể để tránh lỗi ép kiểu trong Cache
            var registrationMeta = new RegistrationMetadata
            {
                UserId = userId,
                TariffId = dto.TariffId,
                SlotId = dto.SlotId,
                LicenseVehicle = cleanLicense,
                DurationMonths = dto.DurationMonths,
                TicketCode = ticketCode,
                StartTime = startTime,
                EndTime = endTime
            };

            // 7. Tạo Invoice PENDING để ghi nhận yêu cầu thanh toán
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

            // 8. Tạo link thanh toán VNPay
            string paymentUrl = _vnPayService.CreatePaymentUrl(
                txnRef: txnRef,
                amount: amountToPay,
                orderInfo: $"DK the thang {cleanLicense} {dto.DurationMonths} thang",
                returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                ipAddress: ipAddress
            );

            // 9. Lưu Metadata trực tiếp vào Cache trên RAM (không lưu DB, không serialize ra JSON)
            _cache.Set(txnRef, registrationMeta, TimeSpan.FromMinutes(15));


            return new MonthlyCardRegistrationResponseDto
            {
                Username = user.Username,
                TicketCode = ticketCode,
                AmountToPay = amountToPay,
                EndTime = endTime,
                PaymentUrl = paymentUrl
            };
        }

        public async Task<PaymentResultDto> ConfirmMonthlyCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            // Sử dụng database transaction để đảm bảo toàn vẹn dữ liệu
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Xác thực hóa đơn PENDING tồn tại trong hệ thống
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Hóa đơn không tồn tại" };
                }

                if (invoice.TotalAmount != amount)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "Số tiền không khớp" };
                }

                if (invoice.PaymentStatus == "SUCCESS" || invoice.PaymentStatus == "FAILED")
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hóa đơn đã được xử lý" };
                }

                // 2. Xử lý khi thanh toán thất bại từ VNPay
                if (responseCode != "00" || transactionStatus != "00")
                {
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ cổng VNPay" };
                }

                // 3. Thanh toán thành công -> Lấy dữ liệu tạm
                if (!_cache.TryGetValue(txnRef, out RegistrationMetadata? meta) || meta == null)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "03", Message = "Không tìm thấy hoặc dữ liệu đăng ký tạm thời đã hết hạn (quá 15 phút)" };
                }

                // Đồng bộ mốc thời gian lưu DB chuẩn múi giờ SE Asia
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var currentDbTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

                // 4. Khởi tạo và Lưu vé (Ticket) với trạng thái Active
                var ticket = new Ticket
                {
                    TicketCode = meta.TicketCode,
                    TicketStatus = ParkingStatuses.TicketActive
                };
                await _context.Tickets.AddAsync(ticket);
                await _context.SaveChangesAsync(); // Lưu để lấy TicketId

                // 4.1. Kiểm tra lại trùng lặp SlotId trước khi lưu để phòng tránh race condition
                var hasActiveMonthly = await _context.MonthlyCards.AnyAsync(mc => mc.SlotId == meta.SlotId && mc.Status == ParkingStatuses.MonthlyCardActive && !mc.IsDeleted);
                if (hasActiveMonthly)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "05", Message = "Chỗ đỗ này đã được đăng ký kích hoạt bởi tài xế khác trong lúc bạn thanh toán." };
                }

                // 5. Khởi tạo và Lưu thẻ tháng (MonthlyCard)
                var monthlyCard = new MonthlyCard
                {
                    UserId = meta.UserId,
                    TariffId = meta.TariffId,
                    TicketId = ticket.TicketId,
                    LicenseVehicle = meta.LicenseVehicle,
                    DurationMonths = meta.DurationMonths,
                    StartTime = currentDbTime,
                    EndTime = currentDbTime.AddMonths(meta.DurationMonths),
                    Status = ParkingStatuses.MonthlyCardActive,
                    IsDeleted = false,
                    SlotId = meta.SlotId
                };
                await _context.MonthlyCards.AddAsync(monthlyCard);

                // Khóa ô đỗ xe bằng cách chuyển sang Reserved nếu nó đang Available
                var slot = await _context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == meta.SlotId && !s.IsDeleted);
                if (slot != null && slot.SlotStatus == ParkingStatuses.SlotAvailable)
                {
                    slot.SlotStatus = ParkingStatuses.SlotReserved;
                    _context.ParkingSlots.Update(slot);
                }

                // 6. Cập nhật hóa đơn sang thành công
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = currentDbTime;
                invoice.UpdatedDate = currentDbTime;

                // 7. Xóa yêu cầu tạm trong IMemoryCache để giải phóng RAM cho Server
                _cache.Remove(txnRef);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // Commit an toàn toàn bộ dữ liệu xuống database vật lý

                _logger.LogInformation("Đăng ký thành công thẻ tháng: Xe {Plate}, Tài xế ID {User}, Hạn dùng: {End}",
                    meta.LicenseVehicle, meta.UserId, monthlyCard.EndTime);

                return new PaymentResultDto { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(); // Hủy bỏ toàn bộ các bước lưu DB trên nếu xảy ra bất kỳ lỗi gì
                _logger.LogError(ex, "Lỗi nghiêm trọng khi kích hoạt thẻ tháng cho giao dịch {TxnRef}", txnRef);
                throw;
            }
        }

        public class RegistrationMetadata
        {
            public int UserId { get; set; }
            public int TariffId { get; set; }
            public int SlotId { get; set; }
            public string LicenseVehicle { get; set; } = null!;
            public int DurationMonths { get; set; }
            public string TicketCode { get; set; } = null!;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }
    }
}
