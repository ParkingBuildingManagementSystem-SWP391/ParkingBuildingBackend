using Google;
using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
                if (invoice.PaymentStatus == "SUCCESS" || invoice.PaymentStatus == "FAILED")
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

                // 5. Cập nhật trạng thái Hóa đơn sang SUCCESS
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;

                _logger.LogInformation("VNPay Confirm: Xác nhận giao dịch {TxnRef} thành công số tiền {Amount} VND. Đang cập nhật trạng thái...",
                    txnRef, amount);

                var session = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (session != null)
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

                // Hoàn tất lưu mọi thay đổi vĩnh viễn vào Database
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("VNPay Confirm: Cập nhật hóa đơn SUCCESS và đồng bộ cơ sở dữ liệu thành công cho giao dịch {TxnRef}.", txnRef);

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

            // 2. Tạo mã giao dịch duy nhất vnp_TxnRef
            string txnRef = "INV" + DateTime.UtcNow.Ticks;

            // 3. Tạo hóa đơn PENDING trong hệ thống
            var invoice = new Invoice
            {
                SessionId = (int)request.SessionId,
                TotalAmount = calculatedFee,
                PaymentMethod = "VNPAY",
                PaymentStatus = "PENDING",
                TransactionCode = txnRef,
                CreatedDate = DateTime.UtcNow
            };

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
            vnpay.AddRequestData("vnp_ReturnUrl", config.ReturnUrl);
            vnpay.AddRequestData("vnp_TxnRef", invoice.TransactionCode);

            string paymentUrl = vnpay.CreateRequestUrl(config.BaseUrl, config.HashSecret);

            _logger.LogInformation("Tạo link VNPay thành công: Phiên {SessionId}, Hóa đơn PENDING {InvoiceId}, Mã giao dịch {TxnRef}. Số tiền: {Amount} VNĐ.",
                session.SessionId, invoice.InvoiceId, txnRef, calculatedFee);

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
            var session = await _sessionRepo.GetByIdAsync(request.SessionId);
            if (session == null || session.SessionStatus == ParkingStatuses.SessionCompleted)
            {
                _logger.LogWarning("Thanh toán tiền mặt thất bại: Phiên đỗ {SessionId} không tồn tại hoặc đã hoàn thành.", request.SessionId);
                return new PaymentResultDto { Success = false, Message = "Phiên đỗ xe không tồn tại hoặc đã được thanh toán" };
            }

            // Kiểm tra xem đã có hóa đơn PENDING được tạo trước đó chưa (luồng checkout hoặc phí phát sinh chênh lệch)
            var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.SessionId == request.SessionId);
            decimal calculatedFee = 0;
            DateTime checkOutTime = session.CheckOutTime ?? DateTime.UtcNow;

            if (existingInvoice != null && existingInvoice.PaymentStatus == "PENDING")
            {
                calculatedFee = existingInvoice.TotalAmount;
            }
            else
            {
                decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);
                DateTime checkInTime = session.CheckInTime ?? DateTime.UtcNow;
                TimeSpan duration = checkOutTime - checkInTime;
                double durationHours = Math.Ceiling(duration.TotalHours);
                if (durationHours <= 0) durationHours = 1;

                calculatedFee = (decimal)durationHours * hourlyRate;
            }

            // Kiểm tra xem số tiền nhân viên nhận từ khách có đủ không
            if (request.AmountReceived < calculatedFee)
            {
                _logger.LogWarning("Thu ngân {StaffId} xác nhận tiền mặt thất bại: Số tiền thực nhận {Received} VNĐ ít hơn số tiền cần thu {Fee} VNĐ cho phiên {SessionId}.",
                    currentStaffId, request.AmountReceived, calculatedFee, request.SessionId);

                return new PaymentResultDto { Success = false, Message = $"Số tiền nhận chưa đủ. Khách cần trả: {calculatedFee:N0} VNĐ" };
            }

            // 2. Tạo hóa đơn thanh toán tiền mặt thành công
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var invoice = new Invoice
                {
                    SessionId = (int)request.SessionId,
                    TotalAmount = calculatedFee,
                    PaymentMethod = "CASH",
                    PaymentStatus = "SUCCESS",
                    StaffId = currentStaffId,
                    CreatedDate = DateTime.UtcNow,
                    PaymentTime = DateTime.UtcNow
                };
                await _invoiceRepo.AddAsync(invoice);

                // 3. Cập nhật trạng thái Session sang Completed
                session.CheckOutTime = checkOutTime;
                session.SessionStatus = ParkingStatuses.SessionCompleted;

                // Cập nhật Trạng thái Vé (Đảm bảo tính toàn vẹn dữ liệu)
                if (session.Ticket != null)
                {
                    session.Ticket.TicketStatus = ParkingStatuses.TicketCompleted;
                }
                await _sessionRepo.UpdateAsync(session);

                // 4. Giải phóng ô đỗ
                var slot = await _slotRepo.GetByIdAsync(session.SlotId);
                if (slot != null)
                {
                    slot.SlotStatus = ParkingStatuses.SlotAvailable;
                    await _slotRepo.UpdateAsync(slot);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                decimal changeDue = request.AmountReceived - calculatedFee;
                string slotName = slot?.SlotName ?? "N/A";
                string license = session.LicenseVehicle ?? "N/A";

                // GHI LOG THÀNH CÔNG ĐỂ ĐỐI SOÁT CUỐI NGÀY
                _logger.LogInformation("ĐỐI SOÁT TIỀN MẶT: Thu ngân {StaffId} đã thu thành công {Amount} VNĐ cho phiên đỗ {SessionId} (Hóa đơn: {InvoiceId}, Xe: {LicensePlate}, Ô đỗ: {SlotName}). Tiền nhận: {Received} VNĐ, Tiền thừa trả lại: {Change} VNĐ.",
                    currentStaffId, calculatedFee, request.SessionId, invoice.InvoiceId, license, slotName, request.AmountReceived, changeDue);

                return new PaymentResultDto
                {
                    Success = true,
                    InvoiceId = invoice.InvoiceId,
                    Message = $"Thanh toán tiền mặt thành công. Số tiền cần trả: {calculatedFee:N0} VNĐ. Đã nhận: {request.AmountReceived:N0} VNĐ. Tiền thừa: {changeDue:N0} VNĐ. Ô đỗ {slotName} đã giải phóng. Mời xe {license} ra khỏi bãi.",
                    ChangeDue = changeDue,
                    LicenseVehicle = license,
                    SlotName = slotName
                };
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();

                // GHI LOG ERROR CỤ THỂ KÈM EXCEPTION STACK TRACE
                _logger.LogError(ex, "Thanh toán tiền mặt thất bại: Lỗi hệ thống khi xử lý thanh toán cho phiên đỗ {SessionId} bởi nhân viên {StaffId}.",
                    request.SessionId, currentStaffId);

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

            return invoice.PaymentStatus;
        }


        // Các phương thức VNPay sẽ được định nghĩa ở các phần tiếp theo...
    }
}