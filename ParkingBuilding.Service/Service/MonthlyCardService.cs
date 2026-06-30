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
            // 1. Lấy thông tin User và gói cước (Tariff) tương ứng
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId && !u.IsDeleted)
                       ?? throw new KeyNotFoundException("Không tìm thấy thông tin tài xế.");

            var tariff = await _context.MonthlyTariffs.FirstOrDefaultAsync(t => t.TariffId == dto.TariffId && !t.IsDeleted)
                         ?? throw new KeyNotFoundException("Gói cước thẻ tháng không tồn tại hoặc đã bị xóa.");

            // 2. Tính toán số tiền phải trả và thời hạn
            decimal amountToPay = tariff.MonthlyPrice * dto.DurationMonths;

            // Định dạng múi giờ Việt Nam để đồng bộ lưu trữ
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var startTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
            var endTime = startTime.AddMonths(dto.DurationMonths);

            // 3. Tạo mã vé ngẫu nhiên cho thẻ tháng
            string ticketCode = $"MC_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";

            // 4. Tạo mã giao dịch VNPay
            string txnRef = "MCR_" + DateTime.UtcNow.Ticks;

            // 5. Đóng gói dữ liệu đăng ký tạm vào class Metadata để lưu cache
            var registrationMeta = new RegistrationMetadata
            {
                UserId = userId,
                TariffId = dto.TariffId,
                DurationMonths = dto.DurationMonths,
                TicketCode = ticketCode,
                StartTime = startTime,
                EndTime = endTime
            };

            // 6. Tạo Invoice PENDING để ghi nhận yêu cầu thanh toán
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

            // 7. Tạo link thanh toán VNPay
            string paymentUrl = _vnPayService.CreatePaymentUrl(
                txnRef: txnRef,
                amount: amountToPay,
                orderInfo: $"DK the thang goi {tariff.TariffId} trong {dto.DurationMonths} thang",
                returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                ipAddress: ipAddress
            );

            // 8. Lưu Metadata trực tiếp vào Cache trên RAM (thời hạn 15 phút chờ thanh toán)
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

                // 3. Thanh toán thành công -> Lấy dữ liệu đăng ký tạm từ Cache
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

                // 5. Khởi tạo và Lưu thẻ tháng (MonthlyCard) không gắn LicenseVehicle và SlotId
                var monthlyCard = new MonthlyCard
                {
                    UserId = meta.UserId,
                    TariffId = meta.TariffId,
                    TicketId = ticket.TicketId,
                    DurationMonths = meta.DurationMonths,
                    StartTime = currentDbTime,
                    EndTime = currentDbTime.AddMonths(meta.DurationMonths),
                    Status = ParkingStatuses.MonthlyCardActive,
                    IsDeleted = false
                };
                await _context.MonthlyCards.AddAsync(monthlyCard);

                // 6. Cập nhật hóa đơn sang thành công
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = currentDbTime;
                invoice.UpdatedDate = currentDbTime;

                // 7. Xóa yêu cầu tạm trong IMemoryCache
                _cache.Remove(txnRef);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // Commit an toàn toàn bộ dữ liệu xuống database

                _logger.LogInformation("Đăng ký thành công thẻ tháng: Tài xế ID {User}, Hạn dùng: {End}",
                    meta.UserId, monthlyCard.EndTime);

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
            public int DurationMonths { get; set; }
            public string TicketCode { get; set; } = null!;
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
        }


        public async Task<object?> GetMyActiveCardAsync(int userId)
        {
            var card = await _context.MonthlyCards
                .Include(c => c.Ticket)
                .Where(c => c.UserId == userId && c.Status == "Active" && !c.IsDeleted)
                .OrderByDescending(c => c.EndTime)
                .Select(c => new {
                    c.MonthlyCardId,
                    c.StartTime,
                    c.EndTime,
                    c.Status,
                    c.TariffId,
                    TicketCode = c.Ticket != null ? c.Ticket.TicketCode : null
                })
                .FirstOrDefaultAsync();

            return card;
        }

    }
}
