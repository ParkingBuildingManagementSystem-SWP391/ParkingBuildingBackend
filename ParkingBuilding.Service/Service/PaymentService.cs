using Google;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace ParkingBuilding.Service.Service
{
    // =========================================================================
    //              LUỒNG 6 : PAYMENT - THANH TOÁN
    // =========================================================================
    /// <summary>
    /// Lớp nghiệp vụ quản lý thanh toán (Payment Workflow).
    /// Hỗ trợ xác nhận thanh toán tiền mặt tại quầy và xử lý giao dịch thông qua cổng thanh toán VNPay.
    /// </summary>
    public class PaymentService : IPaymentService
    {
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISessionRepository _sessionRepo;
        private readonly ISlotRepository _slotRepo;
        private readonly ParkingManagementDbContext _context;
        private readonly ILogger<PaymentService> _logger;
        private readonly IVnPayService _vnPayService;

        public PaymentService(
            IInvoiceRepository invoiceRepo,
            ISessionRepository sessionRepo,
            ISlotRepository slotRepo,
            ParkingManagementDbContext context,
            ILogger<PaymentService> logger,
            IVnPayService vnPayService)
        {
            _invoiceRepo = invoiceRepo;
            _sessionRepo = sessionRepo;
            _slotRepo = slotRepo;
            _context = context;
            _logger = logger;
            _vnPayService = vnPayService;
        }

        /// <summary>
        /// Xử lý Webhook (IPN Callback) từ cổng thanh toán VNPay gửi về khi khách thanh toán thành công.
        /// - Đảm bảo an toàn: Xác minh số tiền khớp và tránh xử lý trùng lặp giao dịch (Double Payment).
        /// - LƯU Ý ĐẶC BIỆT: Nếu khách tự thanh toán trước trên App di động (session.CheckOutTime == null),
        ///   hệ thống chỉ cập nhật hóa đơn SUCCESS mà KHÔNG giải phóng ô đỗ, nhằm tránh tình trạng "trống ảo" khi xe chưa thực sự rời bãi.
        /// </summary>
        public async Task<PaymentResultDto> ConfirmVnPayPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    _logger.LogWarning("VNPay Confirm: Không tìm thấy hóa đơn có mã {TxnRef}", txnRef);

                    return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Invoice không tồn tại" };
                }

                if (invoice.TotalAmount != amount)
                {
                    _logger.LogWarning("VNPay Confirm: Sai số tiền giao dịch {TxnRef}. Hệ thống: {SysAmount}, VNPay: {VnAmount}",
                        txnRef, invoice.TotalAmount, amount);
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "Số tiền không khớp" };
                }

                if (invoice.PaymentStatus == "SUCCESS" || invoice.PaymentStatus == "Deposited" || invoice.PaymentStatus == "FAILED")
                {
                    _logger.LogWarning("VNPay Confirm: Hóa đơn {TxnRef} đã được xử lý trước đó với trạng thái: {Status}.",
                        txnRef, invoice.PaymentStatus);

                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hóa đơn đã được xử lý trước đó" };
                }

                if (responseCode != "00" || transactionStatus != "00")
                {
                    _logger.LogWarning("VNPay Confirm: Thanh toán thất bại cho {TxnRef}. Mã lỗi VNPay: {ResponseCode}, Trạng thái: {Status}",
                        txnRef, responseCode, transactionStatus);

                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ VNPay" };
                }

                bool isDeposit = txnRef.StartsWith("DEP");
                if (isDeposit)
                {
                    invoice.PaymentStatus = "Deposited";
                    invoice.PaymentTime = DateTime.UtcNow;
                    invoice.UpdatedDate = null; 

                    _logger.LogInformation("VNPay Confirm: Xác nhận giao dịch đặt cọc {TxnRef} thành công số tiền {Amount} VND. Đang cập nhật trạng thái...",
                        txnRef, amount);
                }
                else
                {
                    invoice.PaymentStatus = "SUCCESS";
                    invoice.PaymentTime = DateTime.UtcNow;
                    invoice.UpdatedDate = DateTime.UtcNow;

                    _logger.LogInformation("VNPay Confirm: Xác nhận giao dịch thanh toán {TxnRef} thành công số tiền {Amount} VND. Đang cập nhật trạng thái...",txnRef, amount);
                }

                var sessionForFee = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (sessionForFee != null)
                {
                    var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == sessionForFee.TypeId)
                                      ?? throw new Exception("Loại xe của phiên đỗ không tồn tại.");
                    DateTime checkInTime  = sessionForFee.CheckInTime  ?? DateTime.UtcNow;
                    DateTime checkOutTime = sessionForFee.CheckOutTime ?? DateTime.UtcNow;
                    // Fix: EF Core trả về Kind=Unspecified từ SQL Server
                    if (checkInTime.Kind  == DateTimeKind.Unspecified) checkInTime  = DateTime.SpecifyKind(checkInTime,  DateTimeKind.Utc);
                    if (checkOutTime.Kind == DateTimeKind.Unspecified) checkOutTime = DateTime.SpecifyKind(checkOutTime, DateTimeKind.Utc);
                    decimal totalSessionAmount = ParkingPricingCalculator.CalculateFee(checkInTime, checkOutTime, vehicleType);

                    invoice.TotalAmount = totalSessionAmount;
                    _logger.LogInformation("VNPay Confirm: Cập nhật TotalAmount của hóa đơn {TxnRef} thành {TotalAmount} VND (gồm cọc + phí checkout).",
                    txnRef, totalSessionAmount);
                }

                var session = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (session != null)
                {
                    if (isDeposit)
                    {
                        if (session.Ticket != null)
                        {
                            session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
                        }

                        session.SessionStatus = ParkingStatuses.SessionReserved;

                        var slot = await _slotRepo.GetByIdAsync(session.SlotId);
                        if (slot != null)
                        {
                            slot.SlotStatus = ParkingStatuses.SlotReserved;
                            _slotRepo.Update(slot);
                        }

                        _sessionRepo.Update(session);
                        _logger.LogInformation("VNPay Confirm: Đặt chỗ giữ chỗ thành công cho phiên đỗ {SessionId}. Vé {TicketCode} đã kích hoạt.",
                            session.SessionId, session.Ticket?.TicketCode ?? "N/A");
                    }
                    else
                    {
                        if (session.CheckOutTime != null)
                        {
                            session.SessionStatus = ParkingStatuses.SessionCompleted;

                            if (session.Ticket != null)
                            {
                                session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                            }
                            _sessionRepo.Update(session);

                            var slot = await _slotRepo.GetByIdAsync(session.SlotId);
                            if (slot != null)
                            {
                                slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                _slotRepo.Update(slot);
                            }

                            _logger.LogInformation("VNPay Confirm: Hoàn thành phiên đỗ tại cổng {SessionId} và giải phóng ô đỗ {SlotName}.",
                                session.SessionId, slot?.SlotName ?? "N/A");
                        }
                        else
                        {
                            _logger.LogInformation("VNPay Confirm: Tài xế tự thanh toán trước qua App thành công cho phiên đỗ {SessionId}. Trạng thái phiên sẽ được cập nhật khi xe ra tới cổng kiểm soát.",
                                session.SessionId);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("VNPay Confirm: Cập nhật hóa đơn {Status} và đồng bộ cơ sở dữ liệu thành công cho giao dịch {TxnRef}.", invoice.PaymentStatus, txnRef);

                return new PaymentResultDto { Success = true };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                _logger.LogError(ex, "VNPay Confirm: Lỗi hệ thống nghiêm trọng xảy ra khi xử lý xác nhận thanh toán cho giao dịch {TxnRef}", txnRef);

                throw;
            }
        }


        /// <summary>
        /// Tạo hóa đơn PENDING trên hệ thống và trả về Link QR thanh toán VNPay cho tài xế tự thanh toán trước trên App.
        /// </summary>
        public async Task<PaymentResultDto> CreateVnPayPaymentUrlAsync(CreateVnPayPaymentDto request, VnPayConfig config, int currentUserId)
        {
            _logger.LogInformation("Tài xế {UserId} yêu cầu tạo link thanh toán VNPay cho phiên đỗ {SessionId}.", currentUserId, request.SessionId);

            var session = await _sessionRepo.GetByIdAsync(request.SessionId);
            if (session == null || session.SessionStatus == ParkingStatuses.SessionCompleted)
            {
                _logger.LogWarning("Tạo link VNPay thất bại: Phiên đỗ {SessionId} không tồn tại hoặc đã hoàn thành.", request.SessionId);
                return new PaymentResultDto { Success = false, Message = "Phiên đỗ xe không tồn tại hoặc đã được thanh toán" };
            }

            if (session.UserId != currentUserId)
            {
                _logger.LogWarning("CẢNH BÁO BẢO MẬT: Tài xế {UserId} cố tình tạo link thanh toán cho phiên {SessionId} thuộc về tài xế khác (UserId phiên: {OwnerId}).",
                    currentUserId, request.SessionId, session.UserId);

                return new PaymentResultDto { Success = false, Message = "Bạn không có quyền thanh toán cho phiên đỗ xe này!" };
            }

            var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == session.TypeId)
                              ?? throw new Exception("Loại xe của phiên đỗ không tồn tại.");
            DateTime checkInTime = session.CheckInTime ?? DateTime.UtcNow;
            DateTime checkOutTime = DateTime.UtcNow;
            decimal calculatedFee = ParkingPricingCalculator.CalculateFee(checkInTime, checkOutTime, vehicleType);

            var invoice = await _context.Invoices
                           .FirstOrDefaultAsync(i => i.SessionId == request.SessionId && (i.PaymentStatus == "PENDING" || i.PaymentStatus == "FAILED"));
            
            string txnRef;
            decimal finalAmount;
            
            if (invoice != null)
            {
                txnRef = (invoice.TransactionCode != null && invoice.TransactionCode.StartsWith("DEP"))
                                    ? "DEP" + DateTime.UtcNow.Ticks
                                    : "INV" + DateTime.UtcNow.Ticks;
                
                invoice.TransactionCode = txnRef;
                invoice.PaymentStatus = "PENDING";
                invoice.CreatedDate = DateTime.UtcNow;
                invoice.PaymentMethod = "VNPAY";
                _context.Invoices.Update(invoice);
                await _context.SaveChangesAsync();
                finalAmount = invoice.TotalAmount;
            }
            else
            {
                txnRef = "INV" + DateTime.UtcNow.Ticks;
                invoice = new Invoice{
                    SessionId = (int)request.SessionId,
                    TotalAmount = calculatedFee,
                    PaymentMethod = "VNPAY",
                    PaymentStatus = "PENDING",
                    TransactionCode = txnRef,
                    CreatedDate = DateTime.UtcNow
                    };
                await _invoiceRepo.AddAsync(invoice);
                finalAmount = calculatedFee;
            }

            await _invoiceRepo.AddAsync(invoice);

            string paymentUrl = _vnPayService.CreatePaymentUrl(
                txnRef: txnRef,
                amount: calculatedFee,
                orderInfo: $"Thanh toan phi do xe phien {session.SessionId}",
                returnUrl: config.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                ipAddress: request.IpAddress ?? "127.0.0.1"
            );

            _logger.LogInformation("Tạo link VNPay thành công: Phiên {SessionId}, Hóa đơn PENDING {InvoiceId}, Mã giao dịch {TxnRef}. Số tiền: {Amount} VNĐ.",
                session.SessionId, invoice.InvoiceId, txnRef, finalAmount);

            return new PaymentResultDto
            {
                Success = true,
                PaymentUrl = paymentUrl,
                InvoiceId = invoice.InvoiceId
            };
        }


        /// <summary>
        /// Nhân viên xác nhận đã nhận đủ tiền mặt của khách tại cổng ra.
        /// - Ghi nhận hóa đơn SUCCESS bằng phương thức CASH.
        /// - Cập nhật hoàn thành phiên đỗ và giải phóng ô đỗ về Available ngay lập tức.
        /// </summary>
        public async Task<PaymentResultDto> ProcessCashPaymentAsync(CashPaymentDto request, int currentStaffId)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TicketCode))
            {
                _logger.LogWarning("Thanh toán tiền mặt thất bại: Dữ liệu request hoặc TicketCode rỗng.");
                return new PaymentResultDto { Success = false, Message = "Dữ liệu vé thanh toán không hợp lệ!" };
            }

            if (Helpers.QrCodeParserHelper.TryParseQr(request.TicketCode, out var parsedTicket, out var parsedPlate, out var parsedSessionId, out var parsedSlot))
            {
                request.TicketCode = parsedTicket!;
            }

            _logger.LogInformation("Nhân viên {StaffId} bắt đầu xác nhận thanh toán tiền mặt cho vé {TicketCode}.", currentStaffId, request.TicketCode);

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var session = await _context.ParkingSessions
                    .Include(s => s.Slot)
                    .Include(s => s.Ticket)
                    .Include(s => s.Invoice)
                    .FirstOrDefaultAsync(s => s.Ticket != null
                                         && s.Ticket.TicketCode.Trim() == request.TicketCode.Trim()
                                         && s.SessionStatus.Trim() == ParkingStatuses.SessionInProgress
                                         && !s.IsDeleted);

                if (session == null)
                {
                    _logger.LogWarning("Thanh toán tiền mặt thất bại: Không tìm thấy phiên đỗ xe hoạt động cho vé {TicketCode}.", request.TicketCode);
                    await transaction.RollbackAsync();
                    return new PaymentResultDto { Success = false, Message = "Không tìm thấy phiên đỗ xe đang hoạt động với vé này hoặc xe đã ra bãi." };
                }

                var invoice = session.Invoice;
                if (invoice == null)
                {
                    _logger.LogWarning("Thanh toán tiền mặt thất bại: Phiên đỗ {SessionId} không tồn tại hóa đơn liên kết.", session.SessionId);
                    await transaction.RollbackAsync();
                    return new PaymentResultDto { Success = false, Message = "Không tìm thấy hóa đơn cần thanh toán cho phiên này." };
                }

                if (invoice.PaymentStatus != "PENDING" && invoice.PaymentStatus != "FAILED")
                {
                    _logger.LogWarning("Thanh toán tiền mặt thất bại: Hóa đơn {InvoiceId} của phiên {SessionId} có trạng thái là {Status} (yêu cầu PENDING).",
                        invoice.InvoiceId, session.SessionId, invoice.PaymentStatus);
                    await transaction.RollbackAsync();
                    return new PaymentResultDto { Success = false, Message = "Hóa đơn đã được thanh toán hoặc không ở trạng thái chờ thanh toán." };
                }

                decimal requiredAmount = invoice.TotalAmount;

                if (request.AmountReceived < requiredAmount)
                {
                    _logger.LogWarning("Thu ngân {StaffId} xác nhận tiền mặt thất bại: Số tiền thực nhận {Received} VNĐ ít hơn số tiền cần thu {Required} VNĐ cho vé {TicketCode}.",
                        currentStaffId, request.AmountReceived, requiredAmount, request.TicketCode);

                    await transaction.RollbackAsync();
                    return new PaymentResultDto { Success = false, Message = $"Số tiền nhận chưa đủ. Khách cần trả thêm: {requiredAmount:N0} VNĐ" };
                }

                DateTime checkOutTime = session.CheckOutTime ?? DateTime.UtcNow;
                DateTime checkInTime  = session.CheckInTime  ?? DateTime.UtcNow;
                // Fix: EF Core trả về Kind=Unspecified từ SQL Server
                if (checkInTime.Kind  == DateTimeKind.Unspecified) checkInTime  = DateTime.SpecifyKind(checkInTime,  DateTimeKind.Utc);
                if (checkOutTime.Kind == DateTimeKind.Unspecified) checkOutTime = DateTime.SpecifyKind(checkOutTime, DateTimeKind.Utc);

                var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == session.TypeId)
                                  ?? throw new Exception("Loại xe của phiên đỗ không tồn tại.");
                decimal totalSessionAmount = ParkingPricingCalculator.CalculateFee(checkInTime, checkOutTime, vehicleType);

                invoice.TotalAmount = totalSessionAmount; 
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentMethod = "CASH";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;
                invoice.StaffId = currentStaffId;

                session.CheckOutTime = checkOutTime;
                session.SessionStatus = ParkingStatuses.SessionCompleted;

                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }

                var slot = session.Slot;
                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    _context.ParkingSlots.Update(slot);
                }

                _context.Invoices.Update(invoice);
                _context.ParkingSessions.Update(session);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                decimal changeDue = request.AmountReceived - requiredAmount;
                string slotName = slot?.SlotName ?? "N/A";
                string license = session.LicenseVehicle ?? "N/A";

                // Ghi log đối soát tiền mặt chi tiết
                _logger.LogInformation("ĐỐI SOÁT TIỀN MẶT: Thu ngân {StaffId} đã thu thành công {Amount} VNĐ cho phiên đỗ {SessionId} (Hóa đơn: {InvoiceId}, Xe: {LicensePlate}, Ô đỗ: {SlotName}). Tiền nhận: {Received} VNĐ, Tiền thừa trả lại: {Change} VNĐ.",
                    currentStaffId, requiredAmount, session.SessionId, invoice.InvoiceId, license, slotName, request.AmountReceived, changeDue);

                return new PaymentResultDto
                {
                    Success = true,
                    InvoiceId = invoice.InvoiceId,
                    Message = $"Thanh toán tiền mặt thành công. Số tiền cần trả: {requiredAmount:N0} VNĐ. Đã nhận: {request.AmountReceived:N0} VNĐ. Tiền thừa: {changeDue:N0} VNĐ. Ô đỗ {slotName} đã giải phóng. Mời xe {license} ra khỏi bãi.",
                    ChangeDue = changeDue,
                    LicenseVehicle = license,
                    SlotName = slotName
                };
            }
            catch (Exception ex)
            {
                // Hủy bỏ mọi thay đổi nếu xảy ra ngoại lệ
                await transaction.RollbackAsync();

                _logger.LogError(ex, "Thanh toán tiền mặt thất bại: Lỗi hệ thống khi xử lý thanh toán cho vé {TicketCode} bởi nhân viên {StaffId}.",
                    request.TicketCode, currentStaffId);

                throw;
            }
        }



        /// <summary>
        /// Lấy trạng thái thanh toán hiện tại của hóa đơn (PENDING / SUCCESS / FAILED).
        /// - Tài xế (Driver) chỉ được xem hóa đơn của chính mình.
        /// - Nhân viên (Staff) và Quản trị viên (Admin) được xem tất cả hóa đơn.
        /// </summary>
        public async Task<string?> GetPaymentStatusAsync(int invoiceId, int currentUserId, string currentUserRole)
        {
            var invoice = await _invoiceRepo.GetFirstOrDefaultAsync(i => i.InvoiceId == invoiceId, i => i.Session);
            if (invoice == null)
            {
                _logger.LogWarning("Truy vấn trạng thái thanh toán thất bại: Không tìm thấy hóa đơn có ID {InvoiceId}.", invoiceId);
                return null;
            }

            if (currentUserRole == "Registered_Driver" && invoice.Session != null && invoice.Session.UserId != currentUserId)
            {
                _logger.LogWarning("CẢNH BÁO BẢO MẬT: Người dùng {UserId} (Vai trò: {Role}) cố tình truy cập trái phép thông tin hóa đơn {InvoiceId} của phiên đỗ {SessionId} thuộc người khác.",
                    currentUserId, currentUserRole, invoiceId, invoice.SessionId);

                throw new UnauthorizedAccessException("Bạn không có quyền xem thông tin hóa đơn này!");
            }

            _logger.LogInformation("Người dùng {UserId} đã truy vấn thành công trạng thái hóa đơn {InvoiceId}. Trạng thái hiện tại: {Status}.",
                currentUserId, invoiceId, invoice.PaymentStatus);
            if (invoice.PaymentStatus == "SUCCESS")
            {
                if (invoice.TransactionCode != null && invoice.TransactionCode.StartsWith("MCR"))
                {
                    return "SUCCESS_MONTHLY";
                }
                if (invoice.Session != null && invoice.Session.CheckOutTime != null)
                {
                    return "SUCCESS_EXIT";
                }
            }
            
            return invoice.PaymentStatus;
        }

    }
}