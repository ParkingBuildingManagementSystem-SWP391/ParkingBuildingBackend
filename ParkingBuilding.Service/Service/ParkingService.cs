using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;


namespace ParkingBuilding.Service.Service
{
    public class ParkingService : IParkingService
    {
        private readonly IParkingRepository _repository;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ParkingManagementDbContext _context;

        public ParkingService(IParkingRepository repository, IOptions<VnPayConfig> vnPayConfig, ParkingManagementDbContext context)
        {
            _repository = repository;
            _vnPayConfig = vnPayConfig.Value;
            _context = context;
        }

        // ============================================================
        //          LUỒNG 1: XỬ LÝ ĐẶT CHỖ (BOOKING TRÊN WEB) 
        // ============================================================
        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var hasActiveBooking = await _repository.HasActiveReservationAsync(userId);
                if (hasActiveBooking)
                    throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

                // Thay thế hàm GetSlotByIdAsync bằng hàm GetSlotByIdForBookingWithLockAsync
                var slot = await _repository.GetSlotByIdForBookingWithLockAsync(request.SlotId);

                if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot.IsDeleted == true)
                    throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

                if (slot.TypeId != request.TypeId)
                    throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này.");

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
                    BookingTime = DateTime.UtcNow,
                    SessionStatus = ParkingStatuses.SessionReserved,
                    Ticket = ticket,
                    IsDeleted = false
                };

                await _repository.CreateSessionAsync(newSession, slot);
                await transaction.CommitAsync();

                // ============================================
                //  XỬ LÝ SINH MÃ QR TỪ TICKET CODE VỪA TẠO
                // ============================================
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
                    Message = "Đặt chỗ đỗ xe thành công! Vui lòng tới bãi và quét mã check-in trong vòng 15 phút.",
                    TicketCode = ticket.TicketCode,
                    SlotId = newSession.SlotId.ToString(),
                    BookingTime = newSession.BookingTime ?? DateTime.UtcNow,
                    QrCodeBase64 = base64QR
                };
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
        public async Task<bool> CheckInVehicleAsync(ParkingBuilding.Service.DTOs.CheckInRequest request)
        {

            if (request == null) return false;
            string? cleanTicketCode = string.IsNullOrWhiteSpace(request.TicketCode) ? null : request.TicketCode.Trim();
            string? cleanLicense = string.IsNullOrWhiteSpace(request.LicenseVehicle) ? null : request.LicenseVehicle.Trim().ToUpper();

            if (cleanTicketCode == null && cleanLicense == null) return false;

            if (cleanLicense != null)
            {
                var alreadyInParking = await _repository.GetActiveSessionByLicensePlateAsync(cleanLicense);
                if (alreadyInParking != null)
                {
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
                    session = await _repository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _repository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                }

                if (session != null && cleanLicense != null)
                {
                    if (!string.IsNullOrEmpty(session.LicenseVehicle))
                    {
                        if (cleanLicense.ToUpper() != session.LicenseVehicle.Trim().ToUpper())
                        {
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
                session = await _repository.GetReservedSessionByLicenseAsync(cleanLicense);
            }

            if (session == null) return false;

            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow;
            session.CheckInImageUrl = string.IsNullOrWhiteSpace(request.CheckInImageUrl) ? null : request.CheckInImageUrl;
            if (session.Slot != null)
            {
                if (session.Slot.SlotStatus == ParkingStatuses.SlotOccupied)
                {
                    throw new Exception($"Chỗ đỗ {session.Slot.SlotName} hiện đã bị xe khác chiếm dụng. Vui lòng kiểm tra lại vị trí đỗ.");
                }
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            if (cleanTicketCode != null && session.Ticket != null)
            {
                session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
            }

            await _repository.UpdateSessionAndSlotAsync(session, session.Slot!);

            return true;
        }

        // =========================================================================
        //              LUỒNG 3: XỬ LÝ KHÁCH VÃNG LAI (WALK-IN) 
        // =========================================================================
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

            var activeSession = await _repository.GetActiveSessionByLicensePlateAsync(cleanLicense);
            if (activeSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} hiện đang có một lượt đỗ chưa hoàn thành trong bãi!"
                };
            }

            var reservedSession = await _repository.GetReservedSessionByLicenseAsync(cleanLicense);
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
            
            var newSession = await _repository.CreateWalkInSessionWithLockAsync(cleanLicense, request.VehicleTypeId, checkInImageUrl, ticket);
            if (newSession == null)
            {
                return new WalkInResponse { Status = "Full", TicketCode = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!" };
            }
            
            var slot = newSession.Slot ?? await _repository.GetSlotByIdAsync(newSession.SlotId);

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

        public async Task<CheckoutResponse> CheckoutVehicleAsync(CheckoutRequest request, int currentStaffId)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string? cleanTicketCode = (string.IsNullOrEmpty(request.TicketCode) || request.TicketCode.Trim().ToLower() == "string")
                ? null : request.TicketCode.Trim();

            string? cleanCheckoutPlate = (string.IsNullOrEmpty(request.CheckoutLicensePlate) || request.CheckoutLicensePlate.Trim().ToLower() == "string")
                ? null : request.CheckoutLicensePlate.Trim().ToUpper();

            ParkingSession? session = null;

            if (cleanTicketCode != null)
            {
                session = await _repository.GetActiveSessionByTicketCodeAsync(cleanTicketCode);
            }
            else if (request.SessionId.HasValue && request.SessionId.Value > 0)
            {
                session = await _repository.GetActiveSessionByIdAsync(request.SessionId.Value);
            }
            else if (cleanCheckoutPlate != null)
            {
                session = await _repository.GetActiveSessionByLicensePlateAsync(cleanCheckoutPlate);
            }

            if (session == null)
            {
                throw new Exception("Không tìm thấy phiên đỗ xe đang hoạt động phù hợp với thông tin cung cấp.");
            }

            // Cải thiện kiểm tra đầu vào biển số xe lúc ra
            if (string.IsNullOrEmpty(cleanCheckoutPlate))
            {
                throw new Exception("Yêu cầu nhập biển số xe thực tế lúc ra bãi để kiểm tra an ninh đối khớp.");
            }

            string checkInPlate = (session.LicenseVehicle ?? "").Trim().ToUpper();

            // Luôn so sánh biển số xe
            if (checkInPlate != cleanCheckoutPlate)
            {
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

            // =========================================================================
            // KHỐI CODE DƯỚI ĐÂY CHỈ ĐƯỢC CHẠY KHI XE ĐÃ TRÙNG KHỚP BIỂN SỐ AN TOÀN
            // =========================================================================
            var staff = await _repository.GetStaffByIdAsync(currentStaffId);
            string staffName = staff?.Username ?? "Nhân viên hệ thống";

            DateTime checkInTime = session.CheckInTime ?? throw new Exception("Dữ liệu giờ vào của lượt đỗ này không hợp lệ.");
            DateTime checkOutTime = DateTime.UtcNow;

            TimeSpan duration = checkOutTime - checkInTime;
            double durationHours = Math.Ceiling(duration.TotalHours);
            if (durationHours <= 0) durationHours = 1;

            decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);
            decimal totalAmount = (decimal)durationHours * hourlyRate;

            // 1. Kiểm tra xem phiên đỗ xe này đã có hóa đơn thanh toán thành công trước đó chưa
            if (session.Invoice != null && session.Invoice.PaymentStatus == "SUCCESS")
            {
                // 2. Cập nhật giờ ra và trạng thái hoàn thành cho phiên đỗ
                session.CheckOutTime = checkOutTime;
                session.CheckOutImageUrl = (request.CheckOutImageUrl?.Trim().ToLower() == "string") ? null : request.CheckOutImageUrl;
                session.SessionStatus = ParkingStatuses.SessionCompleted;

                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }

                // 3. Giải phóng slot đỗ xe về trạng thái trống (Available)
                var slot = session.Slot ?? await _repository.GetSlotByIdAsync(session.SlotId);
                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                }

                // 4. Lưu tất cả thay đổi vào Database
                await _repository.UpdateSessionAndSlotAsync(session, slot!);

                // 5. Trả về kết quả thông báo xe đã trả trước thành công để mở cổng ra
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
                    TotalAmount = session.Invoice.TotalAmount, // Lấy số tiền thực tế khách đã thanh toán trước
                    StaffName = staffName,
                    InvoiceId = session.Invoice.InvoiceId,
                    IsPaid = true, // Cực kỳ quan trọng: Xác nhận đã trả tiền thành công!
                    PaymentUrl = null
                };
            }



            if (request.PaymentMethod.ToUpper() == "VNPAY")
            {
                // LUỒNG THANH TOÁN ONLINE VNPAY (Chờ quét mã)
                string txnRef = "INV" + DateTime.UtcNow.Ticks;

                var invoice = new Invoice
                {
                    Session = session,
                    TotalAmount = totalAmount,
                    PaymentMethod = "VNPAY",
                    PaymentStatus = "PENDING",
                    TransactionCode = txnRef,
                    CreatedDate = DateTime.UtcNow
                };

                
                session.CheckOutTime = checkOutTime;
                session.CheckOutImageUrl = (request.CheckOutImageUrl?.Trim().ToLower() == "string") ? null : request.CheckOutImageUrl;
                await _repository.UpdateSessionAndSlotAsync(session, null!);
                await _repository.AddInvoiceAsync(invoice);


                // Lưu hóa đơn PENDING, KHÔNG cập nhật trạng thái session và slot
                var vnpay = new VnPayLibrary();
                vnpay.AddRequestData("vnp_Version", _vnPayConfig.Version);
                vnpay.AddRequestData("vnp_Command", _vnPayConfig.Command);
                vnpay.AddRequestData("vnp_TmnCode", _vnPayConfig.TmnCode);
                vnpay.AddRequestData("vnp_Amount", ((long)(totalAmount * 100)).ToString()); // Nhân số tiền với 100 theo quy định của VNPay
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
                vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

                vnpay.AddRequestData("vnp_CurrCode", "VND");
                vnpay.AddRequestData("vnp_IpAddr", "127.0.0.1"); // Hoặc lấy IP thực tế của Client
                vnpay.AddRequestData("vnp_Locale", "vn");
                vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan phi do xe phien {session.SessionId}");
                vnpay.AddRequestData("vnp_OrderType", "other");
                vnpay.AddRequestData("vnp_ReturnUrl", _vnPayConfig.ReturnUrl);
                vnpay.AddRequestData("vnp_TxnRef", txnRef);
                // Tạo URL thanh toán thật
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
                    IsPaid = false, // Chưa thanh toán
                    PaymentUrl = paymentUrl
                };
            }
            else
            {
                // LUỒNG THANH TOÁN TIỀN MẶT (CASH - Chờ nhân viên thu tiền)
                // Tạo hóa đơn ở trạng thái PENDING
                var invoice = new Invoice
                {
                    Session = session,
                    TotalAmount = totalAmount,
                    PaymentTime = null, // Chưa thanh toán
                    StaffId = currentStaffId,
                    CreatedDate = DateTime.UtcNow,
                    PaymentMethod = "CASH",
                    PaymentStatus = "PENDING" // Chờ xác nhận
                };

                // Lưu hóa đơn PENDING và cập nhật thông tin ảnh chụp lúc ra của xe
                session.CheckOutTime = checkOutTime;
                session.CheckOutImageUrl = (request.CheckOutImageUrl?.Trim().ToLower() == "string") ? null : request.CheckOutImageUrl;

                await _repository.UpdateSessionAndSlotAsync(session, null!); // Chưa giải phóng slot
                await _repository.AddInvoiceAsync(invoice);

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
                    IsPaid = false, // CHƯA THANH TOÁN
                    PaymentUrl = null
                };
            }

        }

        // Luong 5 DANH SACH SLOT XE
        public async Task<List<ParkingSlotResponseDto>> GetSlotsByFloorIdAsync(int floorId)
        {
            var slots = await _repository.GetSlotsByFloorIdAsync(floorId);

            return slots.Select(s => new ParkingSlotResponseDto
            {
                SlotName = s.SlotName,
                SlotStatus = s.SlotStatus,
                TypeId = s.TypeId
            }).ToList();
        }
    }
        
}