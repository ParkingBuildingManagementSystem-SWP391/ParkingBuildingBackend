using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Data;
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
        private readonly IWalletService _walletService;

        public BookingService(
            IParkingRepository parkingRepository,
            ParkingManagementDbContext context,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<BookingService> logger,
            IVnPayService vnPayService,
            IWalletService walletService)
        {
            _parkingRepository = parkingRepository;
            _context = context;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _vnPayService = vnPayService;
            _walletService = walletService;
        }

        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            try
            {
                string cleanedVehiclePlate;
                if (request.TypeId == 1) // Xe đạp
                {
                    cleanedVehiclePlate = $"BIKE_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                }
                else
                {
                    if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string cleanedPlate))
                    {
                        throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
                    }
                    cleanedVehiclePlate = cleanedPlate;
                }
                request.LicenseVehicle = cleanedVehiclePlate; 

                // Đưa ExpectedCheckInTime về định dạng UTC trực tiếp (Không dịch chuyển múi giờ)
                if (request.ExpectedCheckInTime.Kind != DateTimeKind.Utc)
                {
                    request.ExpectedCheckInTime = DateTime.SpecifyKind(request.ExpectedCheckInTime, DateTimeKind.Utc);
                }

                // A. LUẬT CHỐNG SPAM: Kiểm tra số lần hủy trong ngày (24 giờ qua)
                var cancelCount = await _context.ParkingSessions
                    .CountAsync(s => s.UserId == userId 
                                  && s.SessionStatus == ParkingStatuses.SessionCanceled 
                                  && s.BookingTime >= DateTime.UtcNow.AddDays(-1));

                if (cancelCount >= 3)
                {
                    throw new Exception("Bạn đã hủy đặt chỗ quá 3 lần trong vòng 24 giờ qua. Tài khoản tạm thời bị khóa chức năng đặt chỗ mới.");
                }

                // B. LUẬT BẢO MẬT: Kiểm tra xem biển số xe này đã có đơn đặt nào đang chờ check-in chưa
                var isPlateActive = await _context.ParkingSessions
                    .AnyAsync(s => s.LicenseVehicle.Trim().ToUpper() == request.LicenseVehicle.Trim().ToUpper()
                                  && (s.SessionStatus.Trim() == ParkingStatuses.SessionReserved
                                      || s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress)
                                  && !s.IsDeleted);

                if (isPlateActive)
                {
                    throw new Exception("Biển số xe này đã được sử dụng cho một lượt đặt chỗ khác đang hoạt động.");
                }

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

                // Nhóm 1: Đặt ngắn hạn (< 2 tiếng) -> Không cọc
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
                        ExpectedCheckInTime = request.ExpectedCheckInTime, // Lưu giờ hẹn đến dạng UTC
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
                // Nhóm 2: Đặt dài hạn (>= 2 tiếng) -> Yêu cầu cọc tiền theo Ca hẹn check-in
                else
                {
                    var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == request.TypeId)
                                      ?? throw new Exception("Loại xe yêu cầu không tồn tại.");

                    // C. CẢI TIẾN TIỀN CỌC: Cọc động theo Ca hẹn check-in
                    var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                    DateTime localCheckIn = TimeZoneInfo.ConvertTimeFromUtc(request.ExpectedCheckInTime, tz);

                    decimal depositAmount = (localCheckIn.Hour >= 18 || localCheckIn.Hour < 6) 
                                            ? vehicleType.NightRate 
                                            : vehicleType.DayRate;

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
                        ExpectedCheckInTime = request.ExpectedCheckInTime, // Lưu giờ hẹn đến dạng UTC
                        CheckInTime = null,
                        CheckOutTime = null,
                        SessionStatus = ParkingStatuses.SessionReserved, 
                        Ticket = ticket,
                        IsDeleted = false
                    };

                    await _parkingRepository.CreateSessionAsync(newSession, slot);

                    string paymentMethod = string.IsNullOrWhiteSpace(request.PaymentMethod)
                        ? "VNPAY"
                        : request.PaymentMethod.Trim().ToUpper();

                    if (paymentMethod != "VNPAY" && paymentMethod != "WALLET" && paymentMethod != "AUTO")
                    {
                        throw new ArgumentException("Phuong thuc thanh toan booking chi ho tro VNPAY, WALLET hoac AUTO.");
                    }

                    if (paymentMethod == "WALLET" || paymentMethod == "AUTO")
                    {
                        bool walletPaymentSuccess = await _walletService.ProcessWalletPaymentAsync(
                            userId,
                            depositAmount,
                            $"Thanh toan coc dat cho phien {newSession.SessionId}");

                        if (!walletPaymentSuccess)
                        {
                            if (paymentMethod == "AUTO")
                            {
                                paymentMethod = "VNPAY";
                            }
                            else
                            {
                                throw new InvalidOperationException("So du vi khong du de thanh toan tien coc dat cho.");
                            }
                        }
                        else
                        {
                            paymentMethod = "WALLET";
                        }
                    }

                    string txnRef = paymentMethod == "WALLET"
                        ? "DEPWALLET" + DateTime.UtcNow.Ticks
                        : "DEP" + DateTime.UtcNow.Ticks; 
                    var invoice = new Invoice
                    {
                        Session = newSession,
                        TotalAmount = depositAmount,
                        PaymentMethod = paymentMethod,
                        PaymentStatus = paymentMethod == "WALLET" ? "Deposited" : "PENDING",
                        TransactionCode = txnRef,
                        CreatedDate = now,
                        PaymentTime = paymentMethod == "WALLET" ? DateTime.UtcNow : null,
                        UpdatedDate = null 
                    };

                    await _context.Invoices.AddAsync(invoice);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Lưu dữ liệu thành công bảng Invoices, hàm bookslotasync của booking");
                    await transaction.CommitAsync();

                    if (paymentMethod == "WALLET")
                    {
                        return new BookSlotResponse
                        {
                            IsSuccess = true,
                            Message = $"Dat cho va thanh toan tien coc {depositAmount:N0} VND bang vi thanh cong.",
                            TicketCode = ticket.TicketCode,
                            SlotName = slot.SlotName,
                            BookingTime = newSession.BookingTime,
                            RequiresPayment = false,
                            PaymentUrl = string.Empty,
                            InvoiceId = invoice.InvoiceId
                        };
                    }

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

        public async Task<CancelBookingResponse> CancelBookingAsync(int userId, int sessionId)
        {
            try
            {
                var session = await _context.ParkingSessions
                    .Include(s => s.Slot)
                    .Include(s => s.Ticket)
                    .Include(s => s.Invoice)
                    .FirstOrDefaultAsync(s => s.SessionId == sessionId && !s.IsDeleted);

                if (session == null)
                    return new CancelBookingResponse { IsSuccess = false, Message = "Không tìm thấy lượt đặt chỗ này." };

                if (session.UserId != userId)
                    return new CancelBookingResponse { IsSuccess = false, Message = "Bạn không có quyền hủy đơn đặt chỗ này." };

                if (session.SessionStatus.Trim() != ParkingStatuses.SessionReserved)
                    return new CancelBookingResponse { IsSuccess = false, Message = "Lượt đặt chỗ đã check-in hoặc đã bị hủy trước đó." };

                var slot = session.Slot;
                if (slot == null)
                    return new CancelBookingResponse { IsSuccess = false, Message = "Không tìm thấy thông tin ô đỗ liên kết với lượt đặt chỗ." };

                slot.SlotStatus = ParkingStatuses.SlotAvailable;

                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketExpired;
                }

                string refundMessage = "Hủy đặt chỗ thành công.";
                if (session.Invoice != null)
                {
                    if (session.Invoice.PaymentStatus == "PENDING")
                    {
                        session.Invoice.PaymentStatus = "FAILED";
                        session.Invoice.UpdatedDate = DateTime.UtcNow;
                    }
                    else if (session.Invoice.PaymentStatus == "Deposited")
                    {
                        // Tịch thu tiền cọc khi tự hủy
                        session.Invoice.PaymentStatus = "FAILED";
                        session.Invoice.UpdatedDate = DateTime.UtcNow;
                        refundMessage = "Hủy đặt chỗ thành công. Tiền cọc giữ chỗ đã thanh toán sẽ không được hoàn lại theo điều khoản dịch vụ.";
                    }
                }

                session.SessionStatus = ParkingStatuses.SessionCanceled;

                await _parkingRepository.UpdateSessionAndSlotAsync(session, slot);

                _logger.LogInformation("Hủy đặt chỗ thành công cho SessionId {SessionId} bởi UserId {UserId}", sessionId, userId);

                return new CancelBookingResponse
                {
                    IsSuccess = true,
                    Message = refundMessage
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra khi hủy đặt chỗ cho SessionId {SessionId} bởi UserId {UserId}: {Message}", sessionId, userId, ex.Message);
                return new CancelBookingResponse { IsSuccess = false, Message = $"Lỗi hệ thống: {ex.Message}" };
            }
        }
    }
}
