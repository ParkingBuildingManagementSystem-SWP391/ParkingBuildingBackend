using Google;
using Microsoft.EntityFrameworkCore;
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

namespace ParkingBuilding.Service.Service
{
    // =========================================================================
    //              LUỒNG 6 : PAYMENT - THANH TOÁN
    // =========================================================================

    public class PaymentService : IPaymentService
    {
        private readonly IInvoiceRepository _invoiceRepo;
        private readonly ISessionRepository _sessionRepo;
        private readonly ISlotRepository _slotRepo;
        private readonly ParkingManagementDbContext _context; // Dùng để quản lý Database Transaction

        public PaymentService(
            IInvoiceRepository invoiceRepo,
            ISessionRepository sessionRepo,
            ISlotRepository slotRepo,
            ParkingManagementDbContext context)
        {
            _invoiceRepo = invoiceRepo;
            _sessionRepo = sessionRepo;
            _slotRepo = slotRepo;
            _context = context;
        }

        public async Task<PaymentResultDto> ConfirmVnPayPaymentAsync(string txnRef, decimal amount, string responseCode)
        {
            // Bắt đầu một Database Transaction để đảm bảo tính toàn vẹn (ACID)
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // 1. Tìm hóa đơn tương ứng với mã TransactionCode (vnp_TxnRef)
                var invoice = await _context.Invoices.FirstOrDefaultAsync(i => i.TransactionCode == txnRef);
                if (invoice == null)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "01", Message = "Invoice không tồn tại" };
                }

                // 2. Kiểm tra số tiền khớp
                if (invoice.TotalAmount != amount)
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "04", Message = "Số tiền không khớp" };
                }

                // 3. Đề phòng xử lý trùng lặp (Double Payment)
                if (invoice.PaymentStatus == "SUCCESS")
                {
                    return new PaymentResultDto { Success = false, ErrorCode = "02", Message = "Hóa đơn đã được xác nhận trước đó" };
                }

                // 4. Kiểm tra mã phản hồi từ VNPay
                if (responseCode != "00")
                {
                    invoice.PaymentStatus = "FAILED";
                    invoice.UpdatedDate = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    return new PaymentResultDto { Success = false, ErrorCode = "00", Message = "Thanh toán thất bại từ VNPay" };
                }

                // 5. Cập nhật trạng thái Hóa đơn
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;

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
                    }
                    // Ngược lại, nếu thanh toán trước trên App di động (session.CheckOutTime == null)
                    // Chúng ta KHÔNG cập nhật trạng thái session và slot tại đây. Việc này sẽ đợi xe ra đến cổng soát vé.
                }

                // Hoàn tất lưu mọi thay đổi vĩnh viễn vào Database
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return new PaymentResultDto { Success = true };
            }
            catch (Exception)
            {
                // Nếu xảy ra bất kỳ lỗi ngoại lệ nào, hủy bỏ toàn bộ thao tác đã thực hiện
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<PaymentResultDto> CreateVnPayPaymentUrlAsync(CreateVnPayPaymentDto request, VnPayConfig config, int currentUserId)
        {
            var session = await _sessionRepo.GetByIdAsync(request.SessionId);
            if (session == null || session.SessionStatus == ParkingStatuses.SessionCompleted)
                return new PaymentResultDto { Success = false, Message = "Phiên đỗ xe không tồn tại hoặc đã được thanh toán" };
            if (session.UserId != currentUserId)
                return new PaymentResultDto { Success = false, Message = "Bạn không có quyền thanh toán cho phiên đỗ xe này!" };

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
            vnpay.AddRequestData("vnp_TxnRef", txnRef);

            string paymentUrl = vnpay.CreateRequestUrl(config.BaseUrl, config.HashSecret);

            return new PaymentResultDto
            {
                Success = true,
                PaymentUrl = paymentUrl,
                InvoiceId = invoice.InvoiceId
            };
        }

        public async Task<PaymentResultDto> ProcessCashPaymentAsync(CashPaymentDto request, int currentStaffId)
        {
            var session = await _sessionRepo.GetByIdAsync(request.SessionId);
            if (session == null || session.SessionStatus == ParkingStatuses.SessionCompleted)
                return new PaymentResultDto { Success = false, Message = "Phiên đỗ xe không tồn tại hoặc đã được thanh toán" };

            decimal hourlyRate = Helpers.PricingHelper.GetHourlyRate(session.TypeId);

            DateTime checkInTime = session.CheckInTime ?? DateTime.UtcNow;
            DateTime checkOutTime = DateTime.UtcNow;
            TimeSpan duration = checkOutTime - checkInTime;
            double durationHours = Math.Ceiling(duration.TotalHours);
            if (durationHours <= 0) durationHours = 1;

            decimal calculatedFee = (decimal)durationHours * hourlyRate;

            // Kiểm tra xem số tiền nhân viên nhận từ khách có đủ không
            if (request.AmountReceived < calculatedFee)
                return new PaymentResultDto { Success = false, Message = $"Số tiền nhận chưa đủ. Khách cần trả: {calculatedFee} VNĐ" };

            // 2. Tạo hóa đơn thanh toán tiền mặt thành công
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 2. Tạo hóa đơn thanh toán tiền mặt thành công
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
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<string?> GetPaymentStatusAsync(int invoiceId, int currentUserId, string currentUserRole)
        {
            var invoice = await _invoiceRepo.GetByIdAsync(invoiceId);
            if (invoice == null) return null;

            // Nếu là tài xế, chỉ cho phép xem hóa đơn thuộc về phiên đỗ của chính mình
            if (currentUserRole == "Registered_Driver" && invoice.Session != null && invoice.Session.UserId != currentUserId)
            {
                throw new UnauthorizedAccessException("Bạn không có quyền xem thông tin hóa đơn này!");
            }

            return invoice.PaymentStatus;
        }

        // Các phương thức VNPay sẽ được định nghĩa ở các phần tiếp theo...
    }
}