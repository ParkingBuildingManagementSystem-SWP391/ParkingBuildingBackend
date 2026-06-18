using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class BookingService : IBookingService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ParkingManagementDbContext _context;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<BookingService> _logger;
        private readonly IVnPayService _vnPayService;

        public BookingService(
            IParkingRepository parkingRepository,
            ParkingManagementDbContext context,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<BookingService> logger,
            IVnPayService vnPayService)
        {
            _parkingRepository = parkingRepository;
            _context = context;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _vnPayService = vnPayService;
        }

        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string cleanedVehiclePlate))
                {
                    throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
                }
                request.LicenseVehicle = cleanedVehiclePlate; 
                var hasActiveBooking = await _parkingRepository.HasActiveReservationAsync(userId, request.TypeId);
                if (hasActiveBooking)
                    throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành cho loại xe này. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

                var slot = await _parkingRepository.GetSlotByIdForBookingWithLockAsync(request.SlotId);
                if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot.IsDeleted == true)
                    throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

                if (slot.TypeId != request.TypeId)
                    throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này.");

                var now = DateTime.UtcNow;
                var diff = request.ExpectedCheckInTime - now;

                if (diff.TotalSeconds <= 0)
                    throw new Exception("Thời gian xe vào dự kiến phải lớn hơn thời gian hiện tại.");

                if (diff.TotalHours < 2)
                {
                    var ticket = new Ticket
                    {
                        TicketCode = $"QR_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                        TicketStatus = ParkingStatuses.TicketActive
                    };

                    slot.SlotStatus = ParkingStatuses.SlotReserved;

                    var newSession = new ParkingSession
                    {
                        UserId = userId,
                        SlotId = request.SlotId,
                        LicenseVehicle = request.LicenseVehicle,
                        TypeId = request.TypeId,
                        BookingTime = now,
                        CheckInTime = null, 
                        CheckOutTime = null,
                        CheckInImageUrl = null,
                        CheckOutImageUrl = null,
                        SessionStatus = ParkingStatuses.SessionReserved, 
                        Ticket = ticket,
                        IsDeleted = false
                    };

                    await _parkingRepository.CreateSessionAsync(newSession, slot);
                    await transaction.CommitAsync();

                    string base64QR = "";
                    using (var qrGenerator = new QRCoder.QRCodeGenerator())
                    using (var qrCodeData = qrGenerator.CreateQrCode(ticket.TicketCode, QRCoder.QRCodeGenerator.ECCLevel.Q))
                    using (var qrCode = new QRCoder.PngByteQRCode(qrCodeData))
                    {
                        byte[] qrCodeBytes = qrCode.GetGraphic(20);
                        base64QR = $"data:image/png;base64,{Convert.ToBase64String(qrCodeBytes)}";
                    }

                    return new BookSlotResponse
                    {
                        IsSuccess = true,
                        Message = "Đặt chỗ đỗ xe thành công! Bạn có thể quét mã check-in khi tới bãi.",
                        TicketCode = ticket.TicketCode,
                        SlotName = slot.SlotName,
                        BookingTime = newSession.BookingTime,
                        QrCodeBase64 = base64QR,
                        RequiresPayment = false
                    };
                }
                else
                {
                    var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == request.TypeId)
                                      ?? throw new Exception("Loại xe yêu cầu không tồn tại.");
                    decimal depositAmount = ParkingPricingCalculator.CalculateFee(now, request.ExpectedCheckInTime, vehicleType);
                    var ticket = new Ticket
                    {
                        TicketCode = $"QR_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                        TicketStatus = ParkingStatuses.TicketActive 
                    };

                    slot.SlotStatus = ParkingStatuses.SlotReserved;

                    var newSession = new ParkingSession
                    {
                        UserId = userId,
                        SlotId = request.SlotId,
                        LicenseVehicle = request.LicenseVehicle,
                        TypeId = request.TypeId,
                        BookingTime = now,
                        CheckInTime = null,
                        CheckOutTime = null,
                        SessionStatus = ParkingStatuses.SessionReserved, 
                        Ticket = ticket,
                        IsDeleted = false
                    };

                    await _parkingRepository.CreateSessionAsync(newSession, slot);

                    string txnRef = "DEP" + DateTime.UtcNow.Ticks; 
                    var invoice = new Invoice
                    {
                        Session = newSession,
                        TotalAmount = depositAmount,
                        PaymentMethod = "VNPAY",
                        PaymentStatus = "PENDING",
                        TransactionCode = txnRef,
                        CreatedDate = now,
                        UpdatedDate = null 
                    };

                    await _context.Invoices.AddAsync(invoice);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Lưu dữ liệu thành công bảng Invoices, hàm bookslotasync của booking");
                    await transaction.CommitAsync();

                    string paymentUrl = _vnPayService.CreatePaymentUrl(
                        txnRef: txnRef,
                        amount: depositAmount,
                        orderInfo: $"Thanh toan dat coc do xe phien {newSession.SessionId}",
                        returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                        ipAddress: "127.0.0.1"
                    );

                    return new BookSlotResponse
                    {
                        IsSuccess = true,
                        Message = $"Thời gian đặt trước trên 2 tiếng. Vui lòng thanh toán số tiền cọc {depositAmount:N0} VND để hoàn tất giữ chỗ.",
                        TicketCode = ticket.TicketCode,
                        SlotName = slot.SlotName,
                        BookingTime = newSession.BookingTime,
                        RequiresPayment = true,
                        PaymentUrl = paymentUrl,
                        InvoiceId = invoice.InvoiceId
                    };
                }
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
