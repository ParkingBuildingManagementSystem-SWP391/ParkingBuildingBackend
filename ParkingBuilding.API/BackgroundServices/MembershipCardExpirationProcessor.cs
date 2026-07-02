using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ParkingBuilding.API.BackgroundServices
{
    /// <summary>
    /// Tiến trình chạy ngầm (Background Service) tự động quét và xử lý các thẻ thành viên đã hết hạn sử dụng,
    /// đồng thời giải phóng các ô đỗ xe bị giữ bởi giao dịch VNPay hết hạn (quá 15 phút).
    /// Hoạt động chu kỳ 1 phút/lần.
    /// </summary>
    public class MembershipCardExpirationProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MembershipCardExpirationProcessor> _logger;

        public MembershipCardExpirationProcessor(IServiceProvider sp, ILogger<MembershipCardExpirationProcessor> logger)
        {
            _serviceProvider = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Membership card expiration processor background service has started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var context = scope.ServiceProvider.GetRequiredService<ParkingManagementDbContext>();

                        // Tính toán thời gian hiện tại theo múi giờ Việt Nam để so khớp với DB
                        var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

                        // 1. Quét và khóa thẻ thành viên đã hết hạn sử dụng, đặt các xe đăng ký về active = 0, giải phóng slot đỗ
                        var expiredCards = await context.MembershipCards
                            .Include(mc => mc.MembershipVehicles)
                            .Include(mc => mc.Slot)
                            .Where(mc => mc.Status == ParkingStatuses.MonthlyCardActive // "Active"
                                         && !mc.IsDeleted
                                         && mc.EndTime < localNow)
                            .ToListAsync(stoppingToken);

                        if (expiredCards.Any())
                        {
                            _logger.LogInformation($"Phát hiện {expiredCards.Count} thẻ thành viên hết hạn sử dụng. Bắt đầu thu hồi tài nguyên...");

                            foreach (var card in expiredCards)
                            {
                                card.Status = ParkingStatuses.MonthlyCardExpired; // "Expired"
                                
                                // Giải phóng chỗ đỗ cố định
                                if (card.Slot != null && (card.Slot.SlotStatus == ParkingStatuses.SlotReserved || card.Slot.SlotStatus == ParkingStatuses.SlotAvailable))
                                {
                                    card.Slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                }

                                // Hủy kích hoạt tất cả biển số xe liên quan
                                foreach (var vehicle in card.MembershipVehicles)
                                {
                                    vehicle.IsActive = false;
                                }

                                _logger.LogInformation($"Đã tự động thu hồi thẻ thành viên ID: {card.MembershipCardId} của tài xế ID: {card.UserId} do hết hạn.");
                            }

                            await context.SaveChangesAsync(stoppingToken);
                        }

                        // 2. Quét và giải phóng chỗ đỗ xe từ các giao dịch VNPay đăng ký membership PENDING đã quá 15 phút
                        var expiredPendingInvoices = await context.Invoices
                            .Where(i => i.PaymentMethod == "VNPAY"
                                        && i.PaymentStatus == "PENDING"
                                        && i.CreatedDate < localNow.AddMinutes(-15)
                                        && i.TransactionCode != null
                                        && i.TransactionCode.StartsWith("MBC_"))
                            .ToListAsync(stoppingToken);

                        if (expiredPendingInvoices.Any())
                        {
                            _logger.LogInformation($"Phát hiện {expiredPendingInvoices.Count} giao dịch đăng ký thẻ thành viên quá hạn 15 phút chưa thanh toán.");

                            foreach (var invoice in expiredPendingInvoices)
                            {
                                invoice.PaymentStatus = "FAILED";
                                invoice.UpdatedDate = localNow;

                                // Parse SlotId từ TransactionCode (MBC_{SlotId}_{Ticks})
                                var parts = invoice.TransactionCode!.Split('_');
                                if (parts.Length >= 2 && int.TryParse(parts[1], out int slotId))
                                {
                                    var slot = await context.ParkingSlots.FirstOrDefaultAsync(s => s.SlotId == slotId, stoppingToken);
                                    if (slot != null && slot.SlotStatus == ParkingStatuses.SlotReserved)
                                    {
                                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                        _logger.LogInformation($"Đã tự động giải phóng ô đỗ {slot.SlotName} (ID: {slot.SlotId}) từ giao dịch VNPay hết hạn {invoice.TransactionCode}.");
                                    }
                                }
                            }

                            await context.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi xảy ra trong vòng quét của MembershipCardExpirationProcessor.");
                }

                // Chạy định kỳ 1 phút/lần
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
