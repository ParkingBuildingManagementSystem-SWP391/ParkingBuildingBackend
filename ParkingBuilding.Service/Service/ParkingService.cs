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

        public ParkingService(IParkingRepository repository, IOptions<VnPayConfig> vnPayConfig)
        {
            _repository = repository;
            _vnPayConfig = vnPayConfig.Value;
        }

        // ============================================================
        //          LUỒNG 1: XỬ LÝ ĐẶT CHỖ (BOOKING TRÊN WEB) 
        // ============================================================
        public async Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request)
        {
            var hasActiveBooking = await _repository.HasActiveReservationAsync(userId);
            if (hasActiveBooking)
                throw new Exception("Bạn đang có một lượt đặt chỗ chưa hoàn thành. Vui lòng check-in hoặc hủy trước khi đặt chỗ mới.");

            var slot = await _repository.GetSlotByIdAsync(request.SlotId);

            if (slot == null || slot.SlotStatus.Trim() != ParkingStatuses.SlotAvailable || slot?.IsDeleted == true)
                throw new Exception("Chỗ đỗ này không còn trống hoặc không tồn tại.");

            if (slot?.TypeId != request.TypeId)
                throw new Exception("Loại xe của bạn không phù hợp với chỗ đỗ này (Ví dụ: Không thể đỗ ô tô vào chỗ xe máy).");

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
            Console.WriteLine($"---> DEBUG Token: UserId = {userId}, SlotId = {request.SlotId}, TypeId = {request.TypeId}");

            await _repository.CreateSessionAsync(newSession, slot);

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
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} hiện đang có một lượt đỗ chưa hoàn thành trong bãi (Chưa Check-out)!"
                };
            }

            var slot = await _repository.GetAvailableSlotForWalkInAsync(request.VehicleTypeId);
            if (slot == null)
                return new WalkInResponse { Status = "Full", TicketCode = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!" };

            var ticket = new Ticket
            {
                TicketCode = $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            slot.SlotStatus = ParkingStatuses.SlotOccupied;

            var newSession = new ParkingSession
            {
                UserId = null,
                SlotId = slot.SlotId,
                LicenseVehicle = cleanLicense,
                TypeId = request.VehicleTypeId,
                CheckInTime = DateTime.UtcNow,
                CheckInImageUrl = (string.IsNullOrWhiteSpace(request.CheckInImageUrl) || request.CheckInImageUrl.Trim().ToLower() == "string") ? null : request.CheckInImageUrl,
                SessionStatus = ParkingStatuses.SessionInProgress,
                Ticket = ticket,
                IsDeleted = false
            };

            await _repository.CreateSessionAsync(newSession, slot);

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

            if (session == null)
            {
                throw new Exception("Không tìm thấy lượt đỗ xe hợp lệ đang ở trong bãi cho phương tiện này.");
            }

            string checkInPlate = (session.LicenseVehicle ?? "").Trim().ToUpper();
            if (!string.IsNullOrEmpty(checkInPlate) && cleanCheckoutPlate != null)
            {
                if (checkInPlate != cleanCheckoutPlate)
                {
                    return new CheckoutResponse
                    {
                        IsSuccess = false,
                        Message = $"HỆ THỐNG CHẶN: Biển số lúc ra ({cleanCheckoutPlate}) không trùng khớp với lúc vào ({checkInPlate})! Nghi ngờ tráo xe gian lận.",
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

            decimal hourlyRate = 2000; // Mặc định cho Xe dap (TypeId = 1)
            if (session.TypeId == 2) // Xe may (TypeId = 2)
            {
                hourlyRate = 5000;
            }
            else if (session.TypeId == 3) // Xe hoi (TypeId = 3)
            {
                hourlyRate = 20000;
            }
            decimal totalAmount = (decimal)durationHours * hourlyRate;

            
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
                vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
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
                // LUỒNG THANH TOÁN TIỀN MẶT (CASH - Hoàn tất checkout ngay)
                session.SessionStatus = ParkingStatuses.SessionCompleted;

                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }

                var slot = session.Slot;
                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                }

                var invoice = new Invoice
                {
                    Session = session,
                    TotalAmount = totalAmount,
                    PaymentTime = checkOutTime,
                    StaffId = currentStaffId,
                    CreatedDate = DateTime.UtcNow,
                    PaymentMethod = "CASH",
                    PaymentStatus = "SUCCESS"
                };

                await _repository.CompleteParkingSessionAsync(session, slot!, invoice);

                return new CheckoutResponse
                {
                    IsSuccess = true,
                    Message = "Xác nhận đối khớp biển số thành công. Mời xe ra.",
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
                    TotalAmount = totalAmount,
                    StaffName = staffName,
                    InvoiceId = invoice.InvoiceId,
                    IsPaid = true // Đã thanh toán thành công bằng tiền mặt
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