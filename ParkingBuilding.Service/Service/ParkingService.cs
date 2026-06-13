using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using Microsoft.Extensions.Logging;
using System;


namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// Lớp nghiệp vụ quản lý hoạt động đỗ xe (Parking Workflow).
    /// Chức năng chính: Đặt chỗ trước, Check-in đặt trước, Check-in vãng lai (Walk-in), Check-out và đối khớp biển số.
    /// </summary>
    public class ParkingService : IParkingService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ISlotRepository _slotRepository;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ParkingManagementDbContext _context;
        private readonly ILogger<ParkingService> _logger;

        public ParkingService(IParkingRepository parkingRepository, ISlotRepository slotRepository, IOptions<VnPayConfig> vnPayConfig, ParkingManagementDbContext context, ILogger<ParkingService> logger)
        {
            _parkingRepository = parkingRepository;
            _slotRepository = slotRepository;
            _vnPayConfig = vnPayConfig.Value;
            _context = context;
            _logger = logger;
        }

        // ============================================================
        //          LUỒNG 1: XỬ LÝ ĐẶT CHỖ (BOOKING TRÊN WEB) 
        // ============================================================
        /// <summary>
        /// LUỒNG 1: Đặt chỗ đỗ xe trước (Booking) qua Web/App cho lái xe đã đăng ký.
        /// - Sử dụng Transaction và UP LOCK để khóa dòng dữ liệu của Slot đỗ, ngăn chặn Race Condition (2 người đặt cùng 1 chỗ).
        /// - Tạo mã vé QR hoạt động ở trạng thái Reserved (Thời gian giữ chỗ tối đa 15 phút).
        /// </summary>
        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Kiểm tra xem người dùng có lượt đặt chỗ nào chưa hoàn thành không
                var hasActiveBooking = await _parkingRepository.HasActiveReservationAsync(userId);
                if (hasActiveBooking)
                    throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

                // 2. Khóa dòng dữ liệu ô đỗ và kiểm tra tính hợp lệ
                var slot = await _parkingRepository.GetSlotByIdForBookingWithLockAsync(request.SlotId);
                if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot.IsDeleted == true)
                    throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

                if (slot.TypeId != request.TypeId)
                    throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này.");

                // 3. Tính toán khoảng thời gian chờ xe vào bãi
                var now = DateTime.UtcNow;
                var diff = request.ExpectedCheckInTime - now;

                if (diff.TotalSeconds <= 0)
                    throw new Exception("Thời gian xe vào dự kiến phải lớn hơn thời gian hiện tại.");

                // TRƯỜNG HỢP 1: DƯỚI 2 TIẾNG -> Đặt trước thành công lập tức không mất tiền
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
                        CheckInTime = null, // Xe chưa vào bãi nên để trống
                        CheckOutTime = null,
                        CheckInImageUrl = null,
                        CheckOutImageUrl = null,
                        SessionStatus = ParkingStatuses.SessionInProgress, // Đặt trạng thái theo yêu cầu
                        Ticket = ticket,
                        IsDeleted = false
                    };

                    await _parkingRepository.CreateSessionAsync(newSession, slot);
                    await transaction.CommitAsync();

                    // Sinh mã QR check-in
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

                // TRƯỜNG HỢP 2: TỪ 2 TIẾNG TRỞ LÊN -> Yêu cầu thanh toán tiền đặt cọc giữ chỗ qua VNPAY
                else
                {
                    // Tính số tiền cọc
                    int extraHours = (int)Math.Floor(diff.TotalHours) - 1;
                    if (extraHours < 1) extraHours = 1;
                    decimal hourlyRate = PricingHelper.GetHourlyRate(request.TypeId);
                    decimal depositAmount = extraHours * hourlyRate;

                    var ticket = new Ticket
                    {
                        TicketCode = $"QR_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                        TicketStatus = ParkingStatuses.TicketActive // Vé tạm thời chưa hoạt động cho đến khi thanh toán xong
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
                        SessionStatus = ParkingStatuses.SessionReserved, // Hoặc một trạng thái PendingPayment riêng
                        Ticket = ticket,
                        IsDeleted = false
                    };

                    await _parkingRepository.CreateSessionAsync(newSession, slot);

                    // Tạo hóa đơn tạm ở trạng thái PENDING
                    string txnRef = "DEP" + DateTime.UtcNow.Ticks; // Tiền tố DEP ký hiệu Deposit đặt cọc
                    var invoice = new Invoice
                    {
                        Session = newSession,
                        TotalAmount = depositAmount,
                        PaymentMethod = "VNPAY",
                        PaymentStatus = "PENDING",
                        TransactionCode = txnRef,
                        CreatedDate = now,
                        UpdatedDate = null // Mặc định khởi tạo để trống
                    };

                    await _context.Invoices.AddAsync(invoice);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Lưu dữ liệu thành công bảng Invoices, hàm bookslotasync của booking");
                    await transaction.CommitAsync();

                    // Khởi tạo tham số gửi lên cổng VNPAY
                    var vnpay = new VnPayLibrary();
                    vnpay.AddRequestData("vnp_Version", _vnPayConfig.Version);
                    vnpay.AddRequestData("vnp_Command", _vnPayConfig.Command);
                    vnpay.AddRequestData("vnp_TmnCode", _vnPayConfig.TmnCode);
                    vnpay.AddRequestData("vnp_Amount", ((long)(depositAmount * 100)).ToString());

                    var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                    var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                    vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

                    vnpay.AddRequestData("vnp_CurrCode", "VND");
                    vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1"); // Có thể lấy IP thực tế của Client
                    vnpay.AddRequestData("vnp_Locale", "vn");
                    vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan dat coc do xe phien {newSession.SessionId}");
                    vnpay.AddRequestData("vnp_OrderType", "other");
                    vnpay.AddRequestData("vnp_ReturnUrl", _vnPayConfig.ReturnUrl);
                    vnpay.AddRequestData("vnp_TxnRef", txnRef);

                    string paymentUrl = vnpay.CreateRequestUrl(_vnPayConfig.BaseUrl, _vnPayConfig.HashSecret);

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

        // =========================================================================
        //              LUỒNG 2: XỬ LÝ KHI XE ĐẾN CỔNG BÃI (CHECK-IN) 
        // =========================================================================
        /// <summary>
        /// LUỒNG 2: Check-in cho xe đã đặt chỗ trước tại cổng vào.
        /// - Hỗ trợ quét bằng mã vé QR hoặc nhận diện biển số xe.
        /// - Đổi trạng thái phiên sang InProgress và chiếm dụng Slot đỗ (SlotOccupied).
        /// </summary>
        public async Task<bool> CheckInVehicleAsync(ParkingBuilding.Service.DTOs.CheckInRequest request)
        {


            if (request == null)
            {
                _logger.LogWarning("Check-in thất bại: Dữ liệu Request rỗng (null).");
                return false;
            }
            _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé/Mã QR: {TicketCode}",
                request.LicenseVehicle, request.TicketCode);


            string? cleanTicketCode = string.IsNullOrWhiteSpace(request.TicketCode) ? null : request.TicketCode.Trim();
            string? cleanLicense = string.IsNullOrWhiteSpace(request.LicenseVehicle) ? null : request.LicenseVehicle.Trim().ToUpper();

            if (cleanTicketCode == null && cleanLicense == null)
            {
                _logger.LogWarning("Check-in thất bại: Cả Biển số xe và Mã vé đều rỗng.");
                return false;
            }

            if (cleanLicense != null)
            {
                var alreadyInParking = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
                if (alreadyInParking != null)
                {
                    _logger.LogWarning("Check-in thất bại: Xe biển số '{LicensePlate}' đã có một phiên đỗ xe đang hoạt động trong bãi (SessionId: {SessionId}).",
               cleanLicense, alreadyInParking.SessionId);
                    return false;
                }
            }

            ParkingSession? session = null;

            // ==============================================================================
            // KỊCH BẢN A: Khách check-in tự động bằng QR/Mã vé đính kèm
            // ==============================================================================
            if (cleanTicketCode != null)
            {
                if (int.TryParse(cleanTicketCode, out int ticketId))
                {
                    session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                }

                if (session != null && cleanLicense != null)
                {
                    if (!string.IsNullOrEmpty(session.LicenseVehicle))
                    {
                        if (cleanLicense.ToUpper() != session.LicenseVehicle.Trim().ToUpper())
                        {
                            _logger.LogWarning("Check-in thất bại: Biển số thực tế '{Actual}' không khớp với biển số đã đăng ký đặt chỗ '{Reserved}' trên vé/Session {SessionId}.",
                       cleanLicense, session.LicenseVehicle, session.SessionId);
                            return false;
                        }
                    }
                }
            }

            // =============================================================================
            // KỊCH BẢN B: Khách check-in bằng Biển số xe (Trường LicenseVehicle có dữ liệu)
            // =============================================================================
            else if (cleanLicense != null)
            {
                session = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
            }

            if (session == null)
            {
                _logger.LogWarning("Check-in thất bại: Không tìm thấy phiên đặt chỗ (Reservation) tương ứng với Biển số '{License}' hoặc Mã vé '{Ticket}'.",
                    cleanLicense, cleanTicketCode);
                return false;
            }


            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow;
            session.CheckInImageUrl = string.IsNullOrWhiteSpace(request.CheckInImageUrl) ? null : request.CheckInImageUrl;
            if (session.Slot != null)
            {
                if (session.Slot.SlotStatus == ParkingStatuses.SlotOccupied)
                {
                    _logger.LogError("Check-in thất bại: Vị trí đỗ {SlotName} đã bị xe khác chiếm dụng cho Session {SessionId}.",
                session.Slot.SlotName, session.SessionId);
                    throw new Exception($"Chỗ đỗ {session.Slot.SlotName} hiện đã bị xe khác chiếm dụng. Vui lòng kiểm tra lại vị trí đỗ.");
                }
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            if (cleanTicketCode != null && session.Ticket != null)
            {
                session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
            }

            await _parkingRepository.UpdateSessionAndSlotAsync(session, session.Slot!);
            _logger.LogInformation("Check-in THÀNH CÔNG: Xe '{LicensePlate}' đã vào bãi. Ô đỗ phân phối: {SlotName}. SessionId: {SessionId}.",
        cleanLicense ?? session.LicenseVehicle, session.Slot?.SlotName ?? "N/A", session.SessionId);
            return true;
        }

        // =========================================================================
        //              LUỒNG 3: XỬ LÝ KHÁCH VÃNG LAI (WALK-IN) 
        // =========================================================================
        /// <summary>
        /// LUỒNG 3: Check-in cho khách vãng lai (Walk-in) tại cổng vào.
        /// - Không đặt chỗ trước. Hệ thống tự động tìm và khóa dòng dữ liệu Slot trống phù hợp với loại xe.
        /// - Tạo vé vãng lai mới và chuyển trạng thái đỗ xe sang InProgress lập tức.
        /// </summary>
        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request)
        {

            if (request == null)
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Yêu cầu dữ liệu không hợp lệ!" };
            }

            string? cleanLicense = (string.IsNullOrWhiteSpace(request.LicenseVehicle) || request.LicenseVehicle.Trim().ToLower() == "string")
            ? null : request.LicenseVehicle.Trim().ToUpper();

            if (request == null || cleanLicense == null)
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Vui lòng nhập biển số xe hợp lệ!" };
            }

            var activeSession = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
            if (activeSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} hiện đang có một lượt đỗ chưa hoàn thành trong bãi!"
                };
            }

            var reservedSession = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
            if (reservedSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} đang có lịch ĐẶT TRƯỚC chưa check-in! Vui lòng quét mã vé đặt trước hoặc thực hiện Check-in theo lịch đặt."
                };
            }

            var ticket = new Ticket
            {
                TicketCode = $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            string? checkInImageUrl = (string.IsNullOrWhiteSpace(request.CheckInImageUrl) || request.CheckInImageUrl.Trim().ToLower() == "string") ? null : request.CheckInImageUrl;
            
            var newSession = await _parkingRepository.CreateWalkInSessionWithLockAsync(cleanLicense, request.VehicleTypeId, checkInImageUrl, ticket);
            if (newSession == null)
            {
                return new WalkInResponse { Status = "Full", TicketCode = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!" };
            }
            
            var slot = newSession.Slot ?? await _parkingRepository.GetSlotByIdAsync(newSession.SlotId);
            if (slot == null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"Hệ thống không tìm thấy slot id tương ứng"
                };
            }

            return new WalkInResponse
            {
                SessionId = newSession.SessionId,
                SlotId = slot.SlotId,
                TicketCode = ticket.TicketCode,
                SlotName = slot.SlotName,
                LicenseVehicle = newSession.LicenseVehicle,
                CheckInTime = newSession.CheckInTime ?? DateTime.UtcNow,
                Status = ParkingStatuses.SessionInProgress
            };
        }

        // =========================================================================
        //              LUỒNG 4: XỬ LÝ KHÁCH CHECK-OUT KHI RỜI BÃI 
        // =========================================================================
        /// <summary>
        /// LUỒNG 4: Kiểm tra an ninh và tính tiền khi xe rời bãi (Check-out).
        /// - Đối khớp biển số lúc vào và lúc ra để chống tráo xe gian lận.
        /// - Làm tròn thời gian đỗ xe lên theo giờ và tính toán tổng phí.
        /// - LƯU Ý ÂN HẠN (Grace Period): Nếu khách đã thanh toán trước qua App:
        ///   + Trong 15 phút: Giải phóng ô đỗ và cho xe ra.
        ///   + Quá 15 phút: Tính toán phí phát sinh thêm, chuyển hóa đơn thành PENDING để thu tiền chênh lệch.
        /// - Nếu chưa thanh toán trước: Tạo hóa đơn PENDING (CASH hoặc VNPAY) chờ nhân viên/khách hàng xử lý.
        /// </summary>
        public async Task<CheckoutResponse> CheckoutVehicleAsync(CheckoutRequest request, int currentStaffId)
        {
            if (request == null)
            {
                _logger.LogWarning("Check-out thất bại: Dữ liệu Request rỗng (null).");
                throw new ArgumentNullException(nameof(request));
            }

            string? cleanTicketCode = (string.IsNullOrEmpty(request.TicketCode) || request.TicketCode.Trim().ToLower() == "string")
                ? null : request.TicketCode.Trim();

            string? cleanCheckoutPlate = (string.IsNullOrEmpty(request.CheckoutLicensePlate) || request.CheckoutLicensePlate.Trim().ToLower() == "string")
                ? null : request.CheckoutLicensePlate.Trim().ToUpper();

            _logger.LogInformation("Bắt đầu xử lý check-out: Vé={TicketCode}, Biển số={Plate}, SessionId={SessionId}, Phương thức thanh toán={Method}",
                cleanTicketCode ?? "N/A", cleanCheckoutPlate ?? "N/A", request.SessionId ?? 0, request.PaymentMethod);

            // Bắt đầu một Database Transaction để đảm bảo tính toàn vẹn (ACID)
            // Nếu có bất cứ bước nào bị lỗi trong chuẩn bị thanh toán, toàn bộ thay đổi sẽ được rollback về ban đầu
            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                ParkingSession? session = null;

                // 1. Truy xuất phiên đỗ xe đang hoạt động (InProgress) dựa trên thông tin đầu vào
                if (cleanTicketCode != null)
                {
                    session = await _parkingRepository.GetActiveSessionByTicketCodeAsync(cleanTicketCode);
                }
                else if (request.SessionId.HasValue && request.SessionId.Value > 0)
                {
                    session = await _parkingRepository.GetActiveSessionByIdAsync(request.SessionId.Value);
                }
                else if (cleanCheckoutPlate != null)
                {
                    session = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanCheckoutPlate);
                }

                if (session == null)
                {
                    _logger.LogWarning("Check-out thất bại: Không tìm thấy phiên đỗ xe đang hoạt động phù hợp.");
                    throw new Exception("Không tìm thấy phiên đỗ xe đang hoạt động phù hợp với thông tin cung cấp.");
                }

                if (string.IsNullOrEmpty(cleanCheckoutPlate))
                {
                    _logger.LogWarning("Check-out thất bại: Bỏ trống biển số xe thực tế lúc ra.");
                    throw new Exception("Yêu cầu nhập biển số xe thực tế lúc ra bãi để kiểm tra an ninh đối khớp.");
                }

                string checkInPlate = (session.LicenseVehicle ?? "").Trim().ToUpper();

                // 2. Đối khớp biển số xe vào bãi và ra bãi để tránh tráo xe gian lận
                if (checkInPlate != cleanCheckoutPlate)
                {
                    // Ghi cảnh báo an ninh kèm thông tin chi tiết
                    _logger.LogWarning("CẢNH BÁO AN NINH: Nghi ngờ tráo xe! Xe ra '{OutPlate}' không khớp xe vào '{InPlate}' tại SessionId {SessionId}.",
                        cleanCheckoutPlate, checkInPlate, session.SessionId);

                    // Rollback để đảm bảo trạng thái không bị sửa đổi ngoài ý muốn
                    await dbTransaction.RollbackAsync();
                    return new CheckoutResponse
                    {
                        IsSuccess = false,
                        Message = $"HỆ THỐNG CHẶN: Biển số lúc ra ({cleanCheckoutPlate}) không khớp lúc vào ({checkInPlate})! Nghi ngờ tráo xe gian lận.",
                        SessionId = session.SessionId,
                        TicketCode = session.Ticket?.TicketCode ?? "N/A",
                        SlotName = session.Slot?.SlotName ?? "N/A",
                        CheckInLicensePlate = checkInPlate,
                        CheckOutLicensePlate = cleanCheckoutPlate,
                        IsLicensePlateMatched = false,
                        CheckInImageUrl = session.CheckInImageUrl,
                        CheckOutImageUrl = request.CheckOutImageUrl,
                        CheckInTime = session.CheckInTime ?? DateTime.UtcNow,
                        CheckOutTime = DateTime.UtcNow,
                        DurationHours = 0,
                        TotalAmount = 0,
                        InvoiceId = 0,
                        IsPaid = false
                    };
                }

                _logger.LogInformation("Đối khớp biển số thành công cho Session {SessionId}.", session.SessionId);

                var staff = await _parkingRepository.GetStaffByIdAsync(currentStaffId);
                string staffName = staff?.Username ?? "Nhân viên hệ thống";

                DateTime checkInTime = session.CheckInTime ?? throw new Exception("Dữ liệu giờ vào của lượt đỗ này không hợp lệ.");
                DateTime checkOutTime = DateTime.UtcNow;

                // 3. Tính toán phí đỗ xe thực tế dựa vào thời gian thực tế đã đỗ (làm tròn lên theo giờ)
                TimeSpan duration = checkOutTime - checkInTime;
                double durationHours = Math.Ceiling(duration.TotalHours);
                if (durationHours <= 0) durationHours = 1;

                decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);
                decimal totalAmount = (decimal)durationHours * hourlyRate;

                // Gán thông tin checkout tạm thời cho Session
                session.CheckOutImageUrl = (request.CheckOutImageUrl?.Trim().ToLower() == "string") ? null : request.CheckOutImageUrl;
                session.CheckOutTime = checkOutTime;

                // ====================================================================================
                // KỊCH BẢN 1: TÀI XẾ ĐÃ TỰ THANH TOÁN TRƯỚC QUA APP & HÓA ĐƠN TRẠNG THÁI "SUCCESS"
                // ====================================================================================
                if (session.Invoice != null && session.Invoice.PaymentStatus == "SUCCESS")
                {
                    var gracePeriod = TimeSpan.FromMinutes(20); // Thời gian ân hạn cho phép xe ra sau khi thanh toán
                    var paymentTime = session.Invoice.PaymentTime ?? session.Invoice.UpdatedDate ?? checkInTime;
                    var timeElapsed = checkOutTime - paymentTime;

                    // Kiểm tra xem đã quá thời gian ân hạn hay chưa
                    if (timeElapsed > gracePeriod)
                    {
                        // Nếu quá 20 phút ân hạn, tính thêm phần phí phát sinh chênh lệch
                        decimal additionalFee = totalAmount - session.Invoice.TotalAmount;

                        if (additionalFee > 0)
                        {
                            _logger.LogInformation("Giao dịch {SessionId} quá thời gian ân hạn {Grace} phút. Phí phát sinh thêm: {AddFee} VNĐ.",
                                session.SessionId, gracePeriod.TotalMinutes, additionalFee);

                            // Chuyển hóa đơn về trạng thái PENDING với số tiền cần thu thêm
                            session.Invoice.PaymentStatus = "PENDING";
                            session.Invoice.TotalAmount = additionalFee; // Đóng tạm thời phí phát sinh để thanh toán khớp số
                            session.Invoice.PaymentTime = null; // Reset để chờ xác nhận thanh toán mới
                            session.Invoice.UpdatedDate = DateTime.UtcNow;
                            session.Invoice.StaffId = currentStaffId;

                            if (request.PaymentMethod.ToUpper() == "VNPAY")
                            {
                                string txnRef = "INV" + DateTime.UtcNow.Ticks;
                                session.Invoice.TransactionCode = txnRef;
                                session.Invoice.PaymentMethod = "VNPAY";

                                _context.ParkingSessions.Update(session);
                                await _context.SaveChangesAsync();
                                await dbTransaction.CommitAsync();

                                // Tạo URL VNPay thanh toán phí phát sinh
                                var vnpay = new VnPayLibrary();
                                vnpay.AddRequestData("vnp_Version", _vnPayConfig.Version);
                                vnpay.AddRequestData("vnp_Command", _vnPayConfig.Command);
                                vnpay.AddRequestData("vnp_TmnCode", _vnPayConfig.TmnCode);
                                vnpay.AddRequestData("vnp_Amount", ((long)(additionalFee * 100)).ToString());
                                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                                var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                                vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

                                vnpay.AddRequestData("vnp_CurrCode", "VND");
                                vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                                vnpay.AddRequestData("vnp_Locale", "vn");
                                vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan phi phat sinh do xe phien {session.SessionId}");
                                vnpay.AddRequestData("vnp_OrderType", "other");
                                vnpay.AddRequestData("vnp_ReturnUrl", _vnPayConfig.ReturnUrl);
                                vnpay.AddRequestData("vnp_TxnRef", txnRef);

                                string paymentUrl = vnpay.CreateRequestUrl(_vnPayConfig.BaseUrl, _vnPayConfig.HashSecret);

                                return new CheckoutResponse
                                {
                                    IsSuccess = true,
                                    Message = $"Quá thời gian ân hạn 20 phút. Vui lòng quét mã QR VNPay để thanh toán thêm phí phát sinh: {additionalFee:N0} VNĐ.",
                                    SessionId = session.SessionId,
                                    TicketCode = session.Ticket?.TicketCode ?? "N/A",
                                    SlotName = session.Slot?.SlotName ?? "N/A",
                                    CheckInLicensePlate = checkInPlate,
                                    CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                                    IsLicensePlateMatched = true,
                                    CheckInImageUrl = session.CheckInImageUrl,
                                    CheckOutImageUrl = session.CheckOutImageUrl,
                                    CheckInTime = checkInTime,
                                    CheckOutTime = checkOutTime,
                                    DurationHours = durationHours,
                                    TotalAmount = additionalFee,
                                    StaffName = staffName,
                                    InvoiceId = session.Invoice.InvoiceId,
                                    IsPaid = false,
                                    PaymentUrl = paymentUrl
                                };
                            }
                            else
                            {
                                // Phương thức CASH (Tiền mặt) thu thêm tại quầy
                                session.Invoice.PaymentMethod = "CASH";
                                _context.ParkingSessions.Update(session);
                                await _context.SaveChangesAsync();
                                await dbTransaction.CommitAsync();

                                return new CheckoutResponse
                                {
                                    IsSuccess = true,
                                    Message = $"Quá thời gian ân hạn 20 phút. Yêu cầu thanh toán thêm phí phát sinh bằng TIỀN MẶT: {additionalFee:N0} VNĐ.",
                                    SessionId = session.SessionId,
                                    TicketCode = session.Ticket?.TicketCode ?? "N/A",
                                    SlotName = session.Slot?.SlotName ?? "N/A",
                                    CheckInLicensePlate = checkInPlate,
                                    CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                                    IsLicensePlateMatched = true,
                                    CheckInImageUrl = session.CheckInImageUrl,
                                    CheckOutImageUrl = session.CheckOutImageUrl,
                                    CheckInTime = checkInTime,
                                    CheckOutTime = checkOutTime,
                                    DurationHours = durationHours,
                                    TotalAmount = additionalFee,
                                    StaffName = staffName,
                                    InvoiceId = session.Invoice.InvoiceId,
                                    IsPaid = false,
                                    PaymentUrl = null
                                };
                            }
                        }
                    }

                    // Nếu vẫn nằm trong 20 phút ân hạn, hoàn thành session ngay lập tức
                    session.SessionStatus = ParkingStatuses.SessionCompleted;

                    if (session.Ticket != null)
                    {
                        session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                    }

                    var slot = session.Slot ?? await _parkingRepository.GetSlotByIdAsync(session.SlotId);
                    if (slot != null)
                    {
                        slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    }

                    _context.ParkingSessions.Update(session);
                    if (slot != null) _context.ParkingSlots.Update(slot);

                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();

                    _logger.LogInformation("Check-out THÀNH CÔNG (Đã thanh toán qua App & Còn hạn): Xe '{Plate}' đã ra bãi. Giải phóng ô {SlotName}. SessionId: {SessionId}.",
                        cleanCheckoutPlate, slot?.SlotName ?? "N/A", session.SessionId);

                    return new CheckoutResponse
                    {
                        IsSuccess = true,
                        Message = "Khách hàng đã tự thanh toán trước qua App thành công. Vui lòng cho xe ra!",
                        SessionId = session.SessionId,
                        TicketCode = session.Ticket?.TicketCode ?? "N/A",
                        SlotName = slot?.SlotName ?? "N/A",
                        CheckInLicensePlate = checkInPlate,
                        CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                        IsLicensePlateMatched = true,
                        CheckInImageUrl = session.CheckInImageUrl,
                        CheckOutImageUrl = session.CheckOutImageUrl,
                        CheckInTime = checkInTime,
                        CheckOutTime = checkOutTime,
                        DurationHours = durationHours,
                        TotalAmount = session.Invoice.TotalAmount,
                        StaffName = staffName,
                        InvoiceId = session.Invoice.InvoiceId,
                        IsPaid = true,
                        PaymentUrl = null
                    };
                }

                // ====================================================================================
                // KỊCH BẢN 2: PHIÊN ĐỖ XE ĐÃ CÓ HÓA ĐƠN TRẠNG THÁI "Deposited" (ĐÃ ĐẶT CỌC GIỮ CHỖ)
                // ====================================================================================
                if (session.Invoice != null && session.Invoice.PaymentStatus == "Deposited")
                {
                    decimal depositAmount = session.Invoice.TotalAmount;
                    decimal additionalAmount = totalAmount - depositAmount; // Tính tiền chênh lệch cần thu thêm

                    _logger.LogInformation("Phát hiện hóa đơn đặt cọc trước cho Session {SessionId}. Số tiền cọc: {Deposit} VND. Tổng phí thực tế: {Total} VND. Cần thu thêm: {Additional} VND.",
                        session.SessionId, depositAmount, totalAmount, additionalAmount);

                    // Kịch bản 2.1: Số tiền cọc đã đủ chi trả toàn bộ phí đỗ xe thực tế
                    if (additionalAmount <= 0)
                    {
                        session.Invoice.PaymentStatus = "SUCCESS";
                        session.Invoice.PaymentTime = DateTime.UtcNow;
                        session.Invoice.UpdatedDate = DateTime.UtcNow;
                        session.Invoice.StaffId = currentStaffId;
                        session.Invoice.PaymentMethod = request.PaymentMethod.ToUpper();

                        session.SessionStatus = ParkingStatuses.SessionCompleted;
                        if (session.Ticket != null)
                        {
                            session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                        }

                        var pSlot = session.Slot ?? await _parkingRepository.GetSlotByIdAsync(session.SlotId);
                        if (pSlot != null)
                        {
                            pSlot.SlotStatus = ParkingStatuses.SlotAvailable;
                            _context.ParkingSlots.Update(pSlot);
                        }

                        _context.ParkingSessions.Update(session);
                        _context.Invoices.Update(session.Invoice);
                        await _context.SaveChangesAsync();
                        await dbTransaction.CommitAsync();

                        _logger.LogInformation("Check-out THÀNH CÔNG (Tiền đặt cọc đủ chi trả): Xe '{Plate}' ra bãi. SessionId: {SessionId}.",
                            cleanCheckoutPlate, session.SessionId);

                        return new CheckoutResponse
                        {
                            IsSuccess = true,
                            Message = "Tiền đặt cọc đã đủ chi trả toàn bộ phí đỗ xe. Vui lòng cho xe ra!",
                            SessionId = session.SessionId,
                            TicketCode = session.Ticket?.TicketCode ?? "N/A",
                            SlotName = pSlot?.SlotName ?? "N/A",
                            CheckInLicensePlate = checkInPlate,
                            CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                            IsLicensePlateMatched = true,
                            CheckInImageUrl = session.CheckInImageUrl,
                            CheckOutImageUrl = session.CheckOutImageUrl,
                            CheckInTime = checkInTime,
                            CheckOutTime = checkOutTime,
                            DurationHours = durationHours,
                            TotalAmount = session.Invoice.TotalAmount,
                            StaffName = staffName,
                            InvoiceId = session.Invoice.InvoiceId,
                            IsPaid = true,
                            PaymentUrl = null
                        };
                    }
                    // Kịch bản 2.2: Phải thu thêm phần chênh lệch phát sinh
                    else
                    {
                        // Cập nhật thông tin hóa đơn hiện tại thành PENDING để chuẩn bị thanh toán
                        session.Invoice.PaymentStatus = "PENDING";
                        session.Invoice.TotalAmount = additionalAmount; // VNPay và quầy Cash sẽ đọc số tiền này để thu
                        session.Invoice.PaymentMethod = request.PaymentMethod.ToUpper();
                        session.Invoice.StaffId = currentStaffId;
                        session.Invoice.UpdatedDate = DateTime.UtcNow;

                        if (request.PaymentMethod.ToUpper() == "VNPAY")
                        {
                            string txnRef = "INV" + DateTime.UtcNow.Ticks;
                            session.Invoice.TransactionCode = txnRef;

                            _context.Invoices.Update(session.Invoice);
                            _context.ParkingSessions.Update(session);
                            await _context.SaveChangesAsync();
                            await dbTransaction.CommitAsync();

                            _logger.LogInformation("Tạo yêu cầu thanh toán VNPay số tiền chênh lệch sau cọc: {Amount} VNĐ cho Session {SessionId}. Mã Ref: {TxnRef}",
                                additionalAmount, session.SessionId, txnRef);

                            // Tạo QR Code thanh toán VNPay cho số tiền chênh lệch
                            var vnpay = new VnPayLibrary();
                            vnpay.AddRequestData("vnp_Version", _vnPayConfig.Version);
                            vnpay.AddRequestData("vnp_Command", _vnPayConfig.Command);
                            vnpay.AddRequestData("vnp_TmnCode", _vnPayConfig.TmnCode);
                            vnpay.AddRequestData("vnp_Amount", ((long)(additionalAmount * 100)).ToString());
                            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                            var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                            vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

                            vnpay.AddRequestData("vnp_CurrCode", "VND");
                            vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                            vnpay.AddRequestData("vnp_Locale", "vn");
                            vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan phi phat sinh do xe phien {session.SessionId}");
                            vnpay.AddRequestData("vnp_OrderType", "other");
                            vnpay.AddRequestData("vnp_ReturnUrl", _vnPayConfig.ReturnUrl);
                            vnpay.AddRequestData("vnp_TxnRef", txnRef);

                            string paymentUrl = vnpay.CreateRequestUrl(_vnPayConfig.BaseUrl, _vnPayConfig.HashSecret);

                            return new CheckoutResponse
                            {
                                IsSuccess = true,
                                Message = $"Cần thanh toán thêm số tiền chênh lệch sau cọc: {additionalAmount:N0} VNĐ. Quét mã QR VNPay để thanh toán.",
                                SessionId = session.SessionId,
                                TicketCode = session.Ticket?.TicketCode ?? "N/A",
                                SlotName = session.Slot?.SlotName ?? "N/A",
                                CheckInLicensePlate = checkInPlate,
                                CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                                IsLicensePlateMatched = true,
                                CheckInImageUrl = session.CheckInImageUrl,
                                CheckOutImageUrl = session.CheckOutImageUrl,
                                CheckInTime = checkInTime,
                                CheckOutTime = checkOutTime,
                                DurationHours = durationHours,
                                TotalAmount = additionalAmount,
                                StaffName = staffName,
                                InvoiceId = session.Invoice.InvoiceId,
                                IsPaid = false,
                                PaymentUrl = paymentUrl
                            };
                        }
                        else
                        {
                            // Trả tiền mặt tại quầy soát vé cho phần chênh lệch
                            _context.Invoices.Update(session.Invoice);
                            _context.ParkingSessions.Update(session);
                            await _context.SaveChangesAsync();
                            await dbTransaction.CommitAsync();

                            _logger.LogInformation("Tạo yêu cầu thanh toán Tiền mặt số tiền chênh lệch sau cọc: {Amount} VNĐ cho Session {SessionId}.",
                                additionalAmount, session.SessionId);

                            return new CheckoutResponse
                            {
                                IsSuccess = true,
                                Message = $"Yêu cầu thanh toán tiền mặt số tiền chênh lệch sau cọc: {additionalAmount:N0} VNĐ.",
                                SessionId = session.SessionId,
                                TicketCode = session.Ticket?.TicketCode ?? "N/A",
                                SlotName = session.Slot?.SlotName ?? "N/A",
                                CheckInLicensePlate = checkInPlate,
                                CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                                IsLicensePlateMatched = true,
                                CheckInImageUrl = session.CheckInImageUrl,
                                CheckOutImageUrl = session.CheckOutImageUrl,
                                CheckInTime = checkInTime,
                                CheckOutTime = checkOutTime,
                                DurationHours = durationHours,
                                TotalAmount = additionalAmount,
                                StaffName = staffName,
                                InvoiceId = session.Invoice.InvoiceId,
                                IsPaid = false,
                                PaymentUrl = null
                            };
                        }
                    }
                }

                // ====================================================================================
                // KỊCH BẢN 3: PHIÊN ĐỖ XE CHƯA CÓ HÓA ĐƠN NÀO ĐƯỢC TẠO (KHÔNG ĐẶT CỌC GIỮ CHỖ)
                // ====================================================================================
                if (request.PaymentMethod.ToUpper() == "VNPAY")
                {
                    string txnRef = "INV" + DateTime.UtcNow.Ticks;

                    var invoice = new Invoice
                    {
                        SessionId = session.SessionId,
                        TotalAmount = totalAmount,
                        PaymentMethod = "VNPAY",
                        PaymentStatus = "PENDING",
                        TransactionCode = txnRef,
                        CreatedDate = DateTime.UtcNow,
                        StaffId = currentStaffId
                    };

                    await _context.Invoices.AddAsync(invoice);
                    _context.ParkingSessions.Update(session);
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();

                    _logger.LogInformation("Tạo yêu cầu thanh toán VNPay lúc ra cho xe {Plate}. Số tiền: {Amount} VNĐ. Mã Ref: {TxnRef}",
                        cleanCheckoutPlate, totalAmount, txnRef);

                    var vnpay = new VnPayLibrary();
                    vnpay.AddRequestData("vnp_Version", _vnPayConfig.Version);
                    vnpay.AddRequestData("vnp_Command", _vnPayConfig.Command);
                    vnpay.AddRequestData("vnp_TmnCode", _vnPayConfig.TmnCode);
                    vnpay.AddRequestData("vnp_Amount", ((long)(totalAmount * 100)).ToString());
                    var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                    var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                    vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

                    vnpay.AddRequestData("vnp_CurrCode", "VND");
                    vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1");
                    vnpay.AddRequestData("vnp_Locale", "vn");
                    vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan phi do xe phien {session.SessionId}");
                    vnpay.AddRequestData("vnp_OrderType", "other");
                    vnpay.AddRequestData("vnp_ReturnUrl", _vnPayConfig.ReturnUrl);
                    vnpay.AddRequestData("vnp_TxnRef", txnRef);

                    string paymentUrl = vnpay.CreateRequestUrl(_vnPayConfig.BaseUrl, _vnPayConfig.HashSecret);

                    return new CheckoutResponse
                    {
                        IsSuccess = true,
                        Message = "Vui lòng quét mã QR VNPay để thanh toán.",
                        SessionId = session.SessionId,
                        TicketCode = session.Ticket?.TicketCode ?? "N/A",
                        SlotName = session.Slot?.SlotName ?? "N/A",
                        CheckInLicensePlate = checkInPlate,
                        CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                        IsLicensePlateMatched = true,
                        CheckInImageUrl = session.CheckInImageUrl,
                        CheckOutImageUrl = session.CheckOutImageUrl,
                        CheckInTime = checkInTime,
                        CheckOutTime = checkOutTime,
                        DurationHours = durationHours,
                        TotalAmount = totalAmount,
                        StaffName = staffName,
                        InvoiceId = invoice.InvoiceId,
                        IsPaid = false,
                        PaymentUrl = paymentUrl
                    };
                }
                else
                {
                    // CASH
                    var invoice = new Invoice
                    {
                        SessionId = session.SessionId,
                        TotalAmount = totalAmount,
                        PaymentTime = null,
                        StaffId = currentStaffId,
                        CreatedDate = DateTime.UtcNow,
                        PaymentMethod = "CASH",
                        PaymentStatus = "PENDING"
                    };

                    await _context.Invoices.AddAsync(invoice);
                    _context.ParkingSessions.Update(session);
                    await _context.SaveChangesAsync();
                    await dbTransaction.CommitAsync();

                    _logger.LogInformation("Tạo yêu cầu thanh toán Tiền mặt lúc ra cho xe {Plate}. Hóa đơn: {InvoiceId}, Thu ngân ID: {StaffId}. Số tiền: {Amount} VNĐ.",
                        cleanCheckoutPlate, invoice.InvoiceId, currentStaffId, totalAmount);

                    return new CheckoutResponse
                    {
                        IsSuccess = true,
                        Message = $"Yêu cầu thanh toán tiền mặt. Số tiền cần thu: {totalAmount} VNĐ.",
                        SessionId = session.SessionId,
                        TicketCode = session.Ticket?.TicketCode ?? "N/A",
                        SlotName = session.Slot?.SlotName ?? "N/A",
                        CheckInLicensePlate = checkInPlate,
                        CheckOutLicensePlate = cleanCheckoutPlate ?? "",
                        IsLicensePlateMatched = true,
                        CheckInImageUrl = session.CheckInImageUrl,
                        CheckOutImageUrl = session.CheckOutImageUrl,
                        CheckInTime = checkInTime,
                        CheckOutTime = checkOutTime,
                        DurationHours = durationHours,
                        TotalAmount = totalAmount,
                        StaffName = staffName,
                        InvoiceId = invoice.InvoiceId,
                        IsPaid = false,
                        PaymentUrl = null
                    };
                }
            }
            catch (Exception ex)
            {
                // Rollback mọi chuẩn bị thanh toán nếu có bất cứ bước nào ném ngoại lệ
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi hệ thống khi xử lý check-out cho SessionId {SessionId} hoặc Ticket {TicketCode}", request.SessionId, request.TicketCode);
                throw;
            }
        }



        // Luong 5 DANH SACH SLOT XE
        public async Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId)
        {
            var slots = await _parkingRepository.GetSlotsByFloorIdAsync(floorId);

            return slots.Select(s => new ParkingSlotResponseDto
            {
                SlotId = s.SlotId,
                SlotName = s.SlotName,
                SlotStatus = s.SlotStatus,
                TypeId = s.TypeId
            }).ToList();
        }



        // Thêm method này vào class ParkingService
        public async Task<MyBookingsDashboardDto> GetMyBookingsAsync(int userId)
        {
            _logger.LogInformation("Bắt đầu lấy danh sách phiên đỗ xe của người dùng {UserId} từ Repository.", userId);

            var sessions = await _parkingRepository.GetSessionsByUserIdAsync(userId);

            _logger.LogInformation("Đã tìm thấy {Count} phiên đỗ xe của người dùng {UserId} từ database.", sessions.Count, userId);

            // 1. Ánh xạ danh sách chi tiết
            var bookingsList = sessions.Select(s => new MyBookingResponseDto
            {
                SessionId = s.SessionId,
                TypeId = s.TypeId,
                BookingTime = s.BookingTime,
                SessionStatus = s.SessionStatus.Trim(),
                FloorName = s.Slot?.Floor?.FloorName ?? "N/A",
                SlotName = s.Slot?.SlotName ?? "N/A",
                LicenseVehicle = s.LicenseVehicle,
                TicketCode = s.Ticket?.TicketCode,
                CheckInTime = s.CheckInTime,
                CheckOutTime = s.CheckOutTime,
                TotalAmount = s.Invoice?.TotalAmount,
                PaymentStatus = s.Invoice?.PaymentStatus,
                PaymentMethod = s.Invoice?.PaymentMethod
            }).ToList();

            // 2. Tính toán tổng hợp số liệu (Summary)
            var dashboard = new MyBookingsDashboardDto
            {
                TotalBookings = bookingsList.Count,
                ActiveBookings = bookingsList.Count(b => b.SessionStatus == "Reserved" || b.SessionStatus == "InProgress"),
                CompletedBookings = bookingsList.Count(b => b.SessionStatus == "Completed"),
                CanceledBookings = bookingsList.Count(b => b.SessionStatus == "Canceled"),
                TotalAmountSpent = bookingsList
                    .Where(b => b.PaymentStatus == "SUCCESS" && b.TotalAmount.HasValue)
                    .Sum(b => b.TotalAmount!.Value),
                BookingsList = bookingsList
            };

            _logger.LogInformation("Thống kê thành công cho người dùng {UserId}. Tổng số tiền chi tiêu: {TotalAmountSpent} VND.", userId, dashboard.TotalAmountSpent);

            return dashboard;
        }


    }

}