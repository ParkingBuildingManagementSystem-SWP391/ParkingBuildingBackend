using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParkingBuilding.API;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;

namespace ParkingBuilding.API.BackgroundServices
{
    public class BookingCancellationProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingCancellationProcessor> _logger;

        public BookingCancellationProcessor(IServiceProvider sp, ILogger<BookingCancellationProcessor> logger)
        {
            _serviceProvider = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Booking Cancellation Processor đã khởi chạy thành công.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var parkingRepo = scope.ServiceProvider.GetRequiredService<IParkingRepository>();

                        // 1. Quét tìm các Session "Reserved" quá hạn 15 phút
                        var expiredSessions = await parkingRepo.GetExpiredReservationsAsync();

                        if (expiredSessions.Any())
                        {
                            _logger.LogInformation($"Phát hiện {expiredSessions.Count} đơn đặt chỗ quá hạn 15 phút. Tiến hành tự động hủy...");

                            foreach (var session in expiredSessions)
                            {
                                try
                                {
                                    // A. Đổi trạng thái lượt đỗ sang Canceled
                                    session.SessionStatus = ParkingStatuses.SessionCanceled;

                                    // B. Giải phóng ô đỗ về Available
                                    var slot = session.Slot;
                                    if (slot != null)
                                    {
                                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                    }

                                    // C. Vô hiệu hóa vé QR sang Expired (Hết hạn)
                                    if (session.Ticket != null)
                                    {
                                        session.Ticket.TicketStatus = ParkingStatuses.TicketExpired;
                                    }

                                    // D. Lưu cập nhật đồng bộ thông qua Repository (đã tích hợp Transaction)
                                    await parkingRepo.UpdateSessionAndSlotAsync(session, slot);

                                    _logger.LogWarning($"Đã tự động hủy lịch đặt của xe: {session.LicenseVehicle} (Session ID: {session.SessionId})");
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, $"Lỗi khi thực thi hủy lượt đỗ ID: {session.SessionId}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi xảy ra trong vòng quét của BookingCancellationProcessor.");
                }

                // Dừng 1 phút trước khi quét lại để không làm nặng máy chủ
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}