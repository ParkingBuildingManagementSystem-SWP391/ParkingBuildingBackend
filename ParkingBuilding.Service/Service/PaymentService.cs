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

        public PaymentService(
            IInvoiceRepository invoiceRepo,
            ISessionRepository sessionRepo,
            ISlotRepository slotRepo,
            ParkingManagementDbContext context,
            ILogger<PaymentService> logger)
        {
            _invoiceRepo = invoiceRepo;
            _sessionRepo = sessionRepo;
            _slotRepo = slotRepo;
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Xử lý Webhook (IPN Callback) từ cổng thanh toán VNPay gửi về khi khách thanh toán thành công.
        /// - Đảm bảo an toàn: Xác minh số tiền khớp và tránh xử lý trùng lặp giao dịch (Double Payment).
        /// - LƯU Ý ĐẶC BIỆT: Nếu khách tự thanh toán trước trên App di động (session.CheckOutTime == null),
        ///   hệ thống chỉ cập nhật hóa đơn SUCCESS mà KHÔNG giải phóng ô đỗ, nhằm tránh tình trạng "trống ảo" khi xe chưa thực sự rời bãi.
        /// </summary>
        public async Task<PaymentResultDto> ConfirmVnPayPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)
        {
            // Bắt đầu một Database Transaction để đảm bảo tính toàn vẹn (ACID)
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Tìm hóa đơn tương ứng với mã TransactionCode (vnp_TxnRef)
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    // GHI LOG WARNING: Không tìm thấy hóa đơn
                    _logger.LogWarning("VNPay Confirm: Không tìm thấy hóa đơn có mã {TxnRef}", txnRef);

                    return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Invoice không tồn tại" };
                }

                // 2. Kiểm tra số tiền khớp
                if (invoice.TotalAmount != amount)
                {
                    // GHI LOG WARNING: Số tiền giao dịch không khớp
                    _logger.LogWarning("VNPay Confirm: Sai số tiền giao dịch {TxnRef}. Hệ thống: {SysAmount}, VNPay: {VnAmount}",
                        txnRef, invoice.TotalAmount, amount);
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "Số tiền không khớp" };
                }

                // 3. Đề phòng xử lý trùng lặp (Double Payment / Đã xử lý)
                if (invoice.PaymentStatus == "SUCCESS" || invoice.PaymentStatus == "Deposited" || invoice.PaymentStatus == "FAILED")
                {
                    // GHI LOG WARNING: Hóa đơn đã được xử lý trước đó
                    _logger.LogWarning("VNPay Confirm: Hóa đơn {TxnRef} đã được xử lý trước đó với trạng thái: {Status}.",
                        txnRef, invoice.PaymentStatus);

                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hóa đơn đã được xử lý trước đó" };
                }

                // 4. Kiểm tra mã phản hồi từ VNPay (Cả vnp_ResponseCode và vnp_TransactionStatus phải bằng "00")
                if (responseCode != "00" || transactionStatus != "00")
                {
                    // GHI LOG WARNING: VNPay xác nhận thanh toán thất bại
                    _logger.LogWarning("VNPay Confirm: Thanh toán thất bại cho {TxnRef}. Mã lỗi VNPay: {ResponseCode}, Trạng thái: {Status}",
                        txnRef, responseCode, transactionStatus);

                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ VNPay" };
                }

                // 5. Cập nhật trạng thái Hóa đơn
                bool isDeposit = txnRef.StartsWith("DEP");
                if (isDeposit)
                {
                    invoice.PaymentStatus = "Deposited";
                    invoice.PaymentTime = DateTime.UtcNow;
                    invoice.UpdatedDate = null; // Đúng yêu cầu đặt cọc giữ chỗ, để trống UpdatedDate

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

                // Tính toán lại tổng tiền thực tế của phiên đỗ xe (Cọc + Phí checkout) để cập nhật vào TotalAmount
                var sessionForFee = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (sessionForFee != null)
                {
                    decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(sessionForFee.TypeId);
                    DateTime checkInTime = sessionForFee.CheckInTime ?? DateTime.UtcNow;
                    DateTime checkOutTime = sessionForFee.CheckOutTime ?? DateTime.UtcNow;
                    TimeSpan duration = checkOutTime - checkInTime;
                    double durationHours = Math.Ceiling(duration.TotalHours);

                    if (durationHours <= 0) durationHours = 1;
                    decimal totalSessionAmount = (decimal)durationHours * hourlyRate;

                    invoice.TotalAmount = totalSessionAmount;
                    _logger.LogInformation("VNPay Confirm: Cập nhật TotalAmount của hóa đơn {TxnRef} thành {TotalAmount} VND (gồm cọc + phí checkout).",
                    txnRef, totalSessionAmount);
                }

                var session = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (session != null)
                {
                    if (isDeposit)
                    {
                        // Cập nhật vé sang trạng thái Active (Có hiệu lực để check-in cổng bãi)
                        if (session.Ticket != null)
                        {
                            session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
                        }

                        // Đặt trạng thái phiên đỗ xe và ô đỗ
                        session.SessionStatus = ParkingStatuses.SessionReserved;

                        var slot = await _slotRepo.GetByIdAsync(session.SlotId);
                        if (slot != null)
                        {
                            slot.SlotStatus = ParkingStatuses.SlotReserved;
                            await _slotRepo.UpdateAsync(slot);
                        }

                        await _sessionRepo.UpdateAsync(session);
                        _logger.LogInformation("VNPay Confirm: Đặt chỗ giữ chỗ thành công cho phiên đỗ {SessionId}. Vé {TicketCode} đã kích hoạt.",
                            session.SessionId, session.Ticket?.TicketCode ?? "N/A");
                    }
                    else
                    {
                        // Nếu là thanh toán tại cổng (CheckOutTime đã được khởi tạo trước đó bởi CheckoutVehicleAsync)
                        if (session.CheckOutTime != null)
                        {
                            session.SessionStatus = ParkingStatuses.SessionCompleted;

                            if (session.Ticket != null)
                            {
                                session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                            }
                            await _sessionRepo.UpdateAsync(session);

                            var slot = await _slotRepo.GetByIdAsync(session.SlotId);
                            if (slot != null)
                            {
                                slot.SlotStatus = ParkingStatuses.SlotAvailable;
                                await _slotRepo.UpdateAsync(slot);
                            }

                            _logger.LogInformation("VNPay Confirm: Hoàn thành phiên đỗ tại cổng {SessionId} và giải phóng ô đỗ {SlotName}.",
                                session.SessionId, slot?.SlotName ?? "N/A");
                        }
                        else
                        {
                            // Ngược lại, nếu thanh toán trước trên App di động (session.CheckOutTime == null)
                            _logger.LogInformation("VNPay Confirm: Tài xế tự thanh toán trước qua App thành công cho phiên đỗ {SessionId}. Trạng thái phiên sẽ được cập nhật khi xe ra tới cổng kiểm soát.",
                                session.SessionId);
                        }
                    }
                }

                // Hoàn tất lưu mọi thay đổi vĩnh viễn vào Database
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("VNPay Confirm: Cập nhật hóa đơn {Status} và đồng bộ cơ sở dữ liệu thành công cho giao dịch {TxnRef}.", invoice.PaymentStatus, txnRef);

                return new PaymentResultDto { Success = true };
            }
            catch (Exception ex)
            {
                // Nếu xảy ra bất kỳ lỗi ngoại lệ nào, hủy bỏ toàn bộ thao tác đã thực hiện
                await transaction.RollbackAsync();

                // GHI LOG ERROR CỤ THỂ KÈM EXCEPTION STACK TRACE
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
                // GHI LOG WARNING BẢO MẬT: Cảnh báo truy cập chéo tài khoản người khác (IDOR)
                _logger.LogWarning("CẢNH BÁO BẢO MẬT: Tài xế {UserId} cố tình tạo link thanh toán cho phiên {SessionId} thuộc về tài xế khác (UserId phiên: {OwnerId}).",
                    currentUserId, request.SessionId, session.UserId);

                return new PaymentResultDto { Success = false, Message = "Bạn không có quyền thanh toán cho phiên đỗ xe này!" };
            }

            decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);

            DateTime checkInTime = session.CheckInTime ?? DateTime.UtcNow;
            DateTime checkOutTime = DateTime.UtcNow;
            TimeSpan duration = checkOutTime - checkInTime;
            double durationHours = Math.Ceiling(duration.TotalHours);
            if (durationHours <= 0) durationHours = 1;

            decimal calculatedFee = (decimal)durationHours * hourlyRate;

            // Tái sử dụng hóa đơn PENDING hoặc FAILED cũ nếu có để tránh tạo trùng bản ghi
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

            // Lưu hóa đơn tạm thời qua IInvoiceRepository
            await _invoiceRepo.AddAsync(invoice);

            // 4. Khởi tạo VnPayLibrary và thêm dữ liệu request
            var vnpay = new VnPayLibrary();
            vnpay.AddRequestData("vnp_Version", config.Version);
            vnpay.AddRequestData("vnp_Command", config.Command);
            vnpay.AddRequestData("vnp_TmnCode", config.TmnCode);
            vnpay.AddRequestData("vnp_Amount", ((long)(calculatedFee * 100)).ToString()); // VNPay nhân số tiền với 100
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var vnNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
            vnpay.AddRequestData("vnp_CreateDate", vnNow.ToString("yyyyMMddHHmmss"));

            vnpay.AddRequestData("vnp_CurrCode", "VND");
            vnpay.AddRequestData("vnp_IpAddr", request.IpAddress ?? "127.0.0.1");
            vnpay.AddRequestData("vnp_Locale", "vn");
            vnpay.AddRequestData("vnp_OrderInfo", $"Thanh toan phi do xe phien {session.SessionId}");
            vnpay.AddRequestData("vnp_OrderType", "other");
            vnpay.AddRequestData("vnp_ReturnUrl", config.ReturnUrl + "?invoiceId=" + invoice.InvoiceId);
            vnpay.AddRequestData("vnp_TxnRef", txnRef);

            string paymentUrl = vnpay.CreateRequestUrl(config.BaseUrl, config.HashSecret);

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

            _logger.LogInformation("Nhân viên {StaffId} bắt đầu xác nhận thanh toán tiền mặt cho vé {TicketCode}.", currentStaffId, request.TicketCode);

            // Bắt đầu một Database Transaction để đảm bảo tính nguyên tử (Atomic)
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Tìm phiên đỗ xe hoạt động liên kết với mã vé (Ticket Code)
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

                // 2. Lấy hóa đơn liên kết đang ở trạng thái PENDING
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

                // Số tiền khách cần đóng lúc check-out (đã được trừ tiền cọc nếu có từ hàm CheckoutVehicleAsync)
                decimal requiredAmount = invoice.TotalAmount;

                // 3. Kiểm tra số tiền nhân viên nhận từ khách có đủ hay không
                if (request.AmountReceived < requiredAmount)
                {
                    _logger.LogWarning("Thu ngân {StaffId} xác nhận tiền mặt thất bại: Số tiền thực nhận {Received} VNĐ ít hơn số tiền cần thu {Required} VNĐ cho vé {TicketCode}.",
                        currentStaffId, request.AmountReceived, requiredAmount, request.TicketCode);

                    await transaction.RollbackAsync();
                    return new PaymentResultDto { Success = false, Message = $"Số tiền nhận chưa đủ. Khách cần trả thêm: {requiredAmount:N0} VNĐ" };
                }

                DateTime checkOutTime = session.CheckOutTime ?? DateTime.UtcNow;
                DateTime checkInTime = session.CheckInTime ?? DateTime.UtcNow;

                // 4. Tính toán tổng chi phí của cả phiên đỗ xe thực tế để ghi nhận tổng số tiền giao dịch cuối cùng
                TimeSpan duration = checkOutTime - checkInTime;
                double durationHours = Math.Ceiling(duration.TotalHours);
                if (durationHours <= 0) durationHours = 1;

                decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);
                decimal totalSessionAmount = (decimal)durationHours * hourlyRate;

                // 5. Cập nhật hóa đơn hiện tại thành thành công (Không tạo dòng mới)
                invoice.TotalAmount = totalSessionAmount; // Ghi nhận tổng số tiền (cọc + tiền mặt đã thu thêm)
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentMethod = "CASH";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;
                invoice.StaffId = currentStaffId;

                // 6. Cập nhật trạng thái Session và Vé xe
                session.CheckOutTime = checkOutTime;
                session.SessionStatus = ParkingStatuses.SessionCompleted;

                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }

                // 7. Giải phóng ô đỗ xe (ParkingSlot)
                var slot = session.Slot;
                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    _context.ParkingSlots.Update(slot);
                }

                // Lưu toàn bộ thay đổi
                _context.Invoices.Update(invoice);
                _context.ParkingSessions.Update(session);
                await _context.SaveChangesAsync();

                // Commit transaction thành công
                await transaction.CommitAsync();

                decimal changeDue = request.AmountReceived - requiredAmount; // Tính tiền thừa trả lại khách
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
            var invoice = await _invoiceRepo.GetByIdAsync(invoiceId);
            if (invoice == null)
            {
                _logger.LogWarning("Truy vấn trạng thái thanh toán thất bại: Không tìm thấy hóa đơn có ID {InvoiceId}.", invoiceId);
                return null;
            }

            // Nếu là tài xế, chỉ cho phép xem hóa đơn thuộc về phiên đỗ của chính mình
            if (currentUserRole == "Registered_Driver" && invoice.Session != null && invoice.Session.UserId != currentUserId)
            {
                // GHI LOG WARNING BẢO MẬT: Phát hiện ý định truy cập trái phép hóa đơn khác (IDOR)
                _logger.LogWarning("CẢNH BÁO BẢO MẬT: Người dùng {UserId} (Vai trò: {Role}) cố tình truy cập trái phép thông tin hóa đơn {InvoiceId} của phiên đỗ {SessionId} thuộc người khác.",
                    currentUserId, currentUserRole, invoiceId, invoice.SessionId);

                throw new UnauthorizedAccessException("Bạn không có quyền xem thông tin hóa đơn này!");
            }

            _logger.LogInformation("Người dùng {UserId} đã truy vấn thành công trạng thái hóa đơn {InvoiceId}. Trạng thái hiện tại: {Status}.",
                currentUserId, invoiceId, invoice.PaymentStatus);
            if (invoice.PaymentStatus == "SUCCESS" && invoice.Session != null && invoice.Session.CheckOutTime != null)
                            {
                                return "SUCCESS_EXIT";
                            }
            
            return invoice.PaymentStatus;
        }


        // Các phương thức VNPay sẽ được định nghĩa ở các phần tiếp theo...
    }
}