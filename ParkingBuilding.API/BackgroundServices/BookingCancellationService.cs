using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParkingBuilding.API;
using ParkingBuilding.Repository.Entities;

namespace ParkingBuilding.API.BackgroundServices
{
    public class BookingCancellationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public BookingCancellationService(IServiceProvider sp) { _serviceProvider = sp; }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<ParkingManagementDbContext>();

                    // Tìm các Session "Reserved" quá 15 phút
                    var timeLimit = DateTime.UtcNow.AddMinutes(-15);

                    var expiredSessions = await dbContext.ParkingSessions
                        .Include(s => s.Slot)
                        .Where(s => s.SessionStatus == "InProgress" && s.BookingTime < timeLimit)
                        .ToListAsync();

                    foreach (var session in expiredSessions)
                    {
                        // Trả lại trạng thái
                        session.SessionStatus = "Canceled";
                        session.Slot.SlotStatus = "Available";
                    }

                    if (expiredSessions.Any()) await dbContext.SaveChangesAsync();
                }

                // Dừng 1 phút trước khi quét lại để không làm nặng máy chủ
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
