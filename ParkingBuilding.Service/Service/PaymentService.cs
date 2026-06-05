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

                // 5. Cập nhật trạng thái Hóa đơn, Phiên đỗ xe và giải phóng Chỗ đỗ tương ứng
                invoice.PaymentStatus = "SUCCESS";
                invoice.PaymentTime = DateTime.UtcNow;
                invoice.UpdatedDate = DateTime.UtcNow;

                var session = await _sessionRepo.GetByIdAsync(invoice.SessionId);
                if (session != null)
                {
                    session.SessionStatus = ParkingStatuses.SessionCompleted;
                    session.CheckOutTime = DateTime.UtcNow;
              
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

        public async Task<PaymentResultDto> CreateVnPayPaymentUrlAsync(CreateVnPayPaymentDto request, VnPayConfig config)
        {
            var session = await _sessionRepo.GetByIdAsync(request.SessionId);
            if (session == null || session.SessionStatus == ParkingStatuses.SessionCompleted)
                return new PaymentResultDto { Success = false, Message = "Phiên đỗ xe không tồn tại hoặc đã được thanh toán" };

            // 1. Tính toán phí đỗ xe dựa trên loại phương tiện (TypeId) và thời gian đỗ
            decimal hourlyRate = 2000; // Mặc định cho Xe đạp (TypeId = 1)
            if (session.TypeId == 2) // Xe máy
            {
                hourlyRate = 5000;
            }
            else if (session.TypeId == 3) // Xe hơi
            {
                hourlyRate = 20000;
            }

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
            vnpay.AddRequestData("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
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

            // 1. Tính toán phí đỗ xe dựa trên loại phương tiện (TypeId) và thời gian đỗ
            decimal hourlyRate = 2000; // Mặc định cho Xe đạp  
            if (session.TypeId == 2) // Xe máy (TypeId = 2)
            {
                hourlyRate = 5000;
            }
            else if (session.TypeId == 3) // Xe hơi (TypeId = 3)
            {
                hourlyRate = 20000;
            }

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
            var invoice = new Invoice
            {
                SessionId = (int)request.SessionId,
                TotalAmount = calculatedFee,
                PaymentMethod = "CASH",
                PaymentStatus = "SUCCESS",
                StaffId = currentStaffId, // Lấy ID nhân viên đã xác thực từ JWT
                CreatedDate = DateTime.UtcNow,
                PaymentTime = DateTime.UtcNow
            };
            await _invoiceRepo.AddAsync(invoice);

            // 3. Cập nhật trạng thái Session sang Completed và giải phóng Slot đỗ xe
            session.CheckOutTime = checkOutTime;
            session.SessionStatus = ParkingStatuses.SessionCompleted;
            await _sessionRepo.UpdateAsync(session);

            var slot = await _slotRepo.GetByIdAsync(session.SlotId);
            if (slot != null)
            {
                slot.SlotStatus = ParkingStatuses.SlotAvailable;
                await _slotRepo.UpdateAsync(slot);
            }

            return new PaymentResultDto { Success = true, InvoiceId = invoice.InvoiceId };
        }

        public async Task<string?> GetPaymentStatusAsync(int invoiceId)
        {
            var invoice = await _invoiceRepo.GetByIdAsync(invoiceId);
            return invoice?.PaymentStatus; // Trả về null nếu không tìm thấy hóa đơn
        }

        // Các phương thức VNPay sẽ được định nghĩa ở các phần tiếp theo...
    }
}