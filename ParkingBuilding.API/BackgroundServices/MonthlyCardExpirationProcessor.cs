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
    /// Tiến trình chạy ngầm (Background Service) tự động quét và xử lý các thẻ tháng đã hết hạn sử dụng.
    /// Hoạt động chu kỳ 1 phút/lần để tự động mở khóa các ô đỗ đính kèm thẻ tháng.
    /// </summary>
    public class MonthlyCardExpirationProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MonthlyCardExpirationProcessor> _logger;

        public MonthlyCardExpirationProcessor(IServiceProvider sp, ILogger<MonthlyCardExpirationProcessor> logger)
        {
            _serviceProvider = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Monthly card expiration processor background service has started.");

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

                        // Tìm các thẻ tháng đang Active nhưng đã hết hạn sử dụng
                        var expiredCards = await context.MonthlyCards
                            .Where(mc => mc.Status == ParkingStatuses.MonthlyCardActive
                                         && !mc.IsDeleted
                                         && mc.EndTime < localNow)
                            .ToListAsync(stoppingToken);

                        if (expiredCards.Any())
                        {
                            _logger.LogInformation($"Phát hiện {expiredCards.Count} thẻ tháng hết hạn sử dụng. Cập nhật trạng thái...");

                            foreach (var card in expiredCards)
                            {
                                card.Status = ParkingStatuses.MonthlyCardExpired;
                            }

                            await context.SaveChangesAsync(stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi xảy ra trong vòng quét của MonthlyCardExpirationProcessor.");
                }

                // Chạy định kỳ 1 phút/lần
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
