using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ParkingBuilding.API;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;

namespace ParkingBuilding.API.BackgroundServices
{
    /// <summary>
    /// Tiến trình chạy ngầm (Background Service) tự động quét và xử lý hủy các đơn đặt chỗ (Booking) đã quá hạn.
    /// Hoạt động chu kỳ 1 phút/lần để giải phóng tài nguyên ô đỗ bị chiếm dụng ảo.
    /// </summary>
    public class BookingCancellationProcessor : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BookingCancellationProcessor> _logger;

        public BookingCancellationProcessor(IServiceProvider sp, ILogger<BookingCancellationProcessor> logger)
        {
            _serviceProvider = sp;
            _logger = logger;
        }

        /// <summary>
        /// Vòng lặp thực thi của tiến trình chạy ngầm.
        /// - Quét CSDL tìm các lượt đặt chỗ (Reserved) đã quá 15 phút so với BookingTime mà khách chưa đến check-in.
        /// - Chuyển trạng thái phiên sang Canceled, khôi phục trạng thái Slot về Available và đổi trạng thái Vé sang Expired.
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("The cancellation process has successfully begun.............");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var parkingRepo = scope.ServiceProvider.GetRequiredService<IParkingRepository>();

                        var expiredSessions = await parkingRepo.GetExpiredReservationsAsync();

                        if (expiredSessions.Any())
                        {
                            _logger.LogInformation($"Phát hiện {expiredSessions.Count} đơn đặt chỗ quá hạn 15 phút. Tiến hành tự động hủy...");

                            foreach (var session in expiredSessions)
                            {
                                try
                                {
                                    session.SessionStatus = ParkingStatuses.SessionCanceled;
                                    var slot = session.Slot;
                                    if (slot != null)
                                    {
                                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                    }
                                    if (session.Ticket != null)
                                    {
                                        session.Ticket.TicketStatus = ParkingStatuses.TicketExpired;
                                    }
                                    // Cập nhật trạng thái hóa đơn sang FAILED nếu là hóa đơn cọc quá hạn chưa thanh toán
                                    if (session.Invoice != null)
                                    {
                                        session.Invoice.PaymentStatus = "FAILED";
                                        session.Invoice.UpdatedDate = DateTime.UtcNow;
                                    }
                                    // Hàm UpdateSessionAndSlotAsync sử dụng SaveChangesAsync sẽ lưu luôn thay đổi của session.Invoice vào DB
                                    await parkingRepo.UpdateSessionAndSlotAsync(session, slot);
                                    _logger.LogWarning($"Đã tự động hủy lịch đặt của xe: {session.LicenseVehicle} (Session ID: {session.SessionId}) do không thanh toán cọc hoặc không check-in đúng hẹn.");
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

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}