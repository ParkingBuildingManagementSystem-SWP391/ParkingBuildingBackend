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
    public class CheckOutService : ICheckOutService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ParkingManagementDbContext _context;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<CheckOutService> _logger;
        private readonly IVnPayService _vnPayService;

        public CheckOutService(
            IParkingRepository parkingRepository,
            ParkingManagementDbContext context,
            IOptions<VnPayConfig> vnPayConfig,
            ILogger<CheckOutService> logger,
            IVnPayService vnPayService)
        {
            _parkingRepository = parkingRepository;
            _context = context;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
            _vnPayService = vnPayService;
        }

        public async Task<CheckoutResponse> CheckoutVehicleAsync(CheckoutRequest request, int currentStaffId)
        {
            if (request == null)
            {
                _logger.LogWarning("Check-out thất bại: Dữ liệu Request rỗng (null).");
                throw new ArgumentNullException(nameof(request));
            }

            string cleanTicketCode = request.TicketCode.Trim();
            if (string.IsNullOrEmpty(cleanTicketCode) || cleanTicketCode.ToLower() == "string")
            {
                throw new ArgumentException("Yêu cầu nhập mã vé (TicketCode) hợp lệ.");
            }
            
            if (string.IsNullOrWhiteSpace(request.CheckoutLicensePlate) || request.CheckoutLicensePlate.Trim().ToLower() == "string")
            {
                throw new ArgumentException("Yêu cầu nhập biển số xe thực tế lúc ra bãi để kiểm tra an ninh đối khớp.");
            }
            
            if (!LicensePlateHelper.IsValidLicensePlate(request.CheckoutLicensePlate, out string cleanedCheckoutPlate))
            {
                throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
            }
            string cleanCheckoutPlate = cleanedCheckoutPlate;
            
            _logger.LogInformation("Bắt đầu xử lý check-out: Vé={TicketCode}, Biển số={Plate}, Phương thức thanh toán={Method}",
            cleanTicketCode, cleanCheckoutPlate, request.PaymentMethod);
            
            using var dbTransaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Tìm kiếm phiên đỗ xe đang hoạt động (SessionInProgress) dựa trên TicketCode
                ParkingSession ? session = await _parkingRepository.GetActiveSessionByTicketCodeAsync(cleanTicketCode);
                
                if (session == null)
                {
                    _logger.LogWarning("Check-out thất bại: Không tìm thấy phiên đỗ xe hoạt động cho vé {TicketCode}.", cleanTicketCode);
                    throw new Exception("Không tìm thấy phiên đỗ xe đang hoạt động phù hợp với mã vé cung cấp.");
                }

                if (string.IsNullOrEmpty(cleanCheckoutPlate))
                {
                    _logger.LogWarning("Check-out thất bại: Bỏ trống biển số xe thực tế lúc ra.");
                    throw new Exception("Yêu cầu nhập biển số xe thực tế lúc ra bãi để kiểm tra an ninh đối khớp.");
                }

                string checkInPlate = (session.LicenseVehicle ?? "").Trim().ToUpper();

                if (checkInPlate != cleanCheckoutPlate)
                {
                    _logger.LogWarning("CẢNH BÁO AN NINH: Nghi ngờ tráo xe! Xe ra '{OutPlate}' không khớp xe vào '{InPlate}' tại SessionId {SessionId}.",
                        cleanCheckoutPlate, checkInPlate, session.SessionId);

                    await dbTransaction.RollbackAsync();
                    return new CheckoutResponse
                    {
                        IsSuccess = false,
                        Message = $"HỆ THỐNG CHẶN: Biển số lúc ra ({cleanCheckoutPlate}) không khớp lúc vào ({checkInPlate})! Nghi ngờ tráo xe gian lận.",
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

                TimeSpan duration = checkOutTime - checkInTime;
                double durationHours = Math.Ceiling(duration.TotalHours);
                if (durationHours <= 0) durationHours = 1;

                var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == session.TypeId)
                                  ?? throw new Exception("Loại xe của phiên đỗ không tồn tại.");
                decimal totalAmount = ParkingPricingCalculator.CalculateFee(checkInTime, checkOutTime, vehicleType);

                session.CheckOutImageUrl = (request.CheckOutImageUrl?.Trim().ToLower() == "string") ? null : request.CheckOutImageUrl;
                session.CheckOutTime = checkOutTime;

                // ====================================================================================
                // KỊCH BẢN 1: TÀI XẾ ĐÃ TỰ THANH TOÁN TRƯỚC QUA APP & HÓA ĐƠN TRẠNG THÁI "SUCCESS"
                // ====================================================================================
                if (session.Invoice != null && session.Invoice.PaymentStatus == "SUCCESS")
                {
                    var gracePeriod = TimeSpan.FromMinutes(20); 
                    var paymentTime = session.Invoice.PaymentTime ?? session.Invoice.UpdatedDate ?? checkInTime;
                    var timeElapsed = checkOutTime - paymentTime;

                    if (timeElapsed > gracePeriod)
                    {
                        decimal additionalFee = totalAmount - session.Invoice.TotalAmount;

                        if (additionalFee > 0)
                        {
                            _logger.LogInformation("Giao dịch {SessionId} quá thời gian ân hạn {Grace} phút. Phí phát sinh thêm: {AddFee} VNĐ.",
                                session.SessionId, gracePeriod.TotalMinutes, additionalFee);

                            session.Invoice.PaymentStatus = "PENDING";
                            session.Invoice.TotalAmount = additionalFee; 
                            session.Invoice.PaymentTime = null;
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

                                string paymentUrl = _vnPayService.CreatePaymentUrl(
                                    txnRef: txnRef,
                                    amount: additionalFee,
                                    orderInfo: $"Thanh toan phi phat sinh do xe phien {session.SessionId}",
                                    returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + session.Invoice.InvoiceId,
                                    ipAddress: "127.0.0.1"
                                );

                                return new CheckoutResponse
                                {
                                    IsSuccess = true,
                                    Message = $"Quá thời gian ân hạn 20 phút. Vui lòng quét mã QR VNPay để thanh toán thêm phí phát sinh: {additionalFee:N0} VNĐ.",
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
                                session.Invoice.PaymentMethod = "CASH";
                                _context.ParkingSessions.Update(session);
                                await _context.SaveChangesAsync();
                                await dbTransaction.CommitAsync();

                                return new CheckoutResponse
                                {
                                    IsSuccess = true,
                                    Message = $"Quá thời gian ân hạn 20 phút. Yêu cầu thanh toán thêm phí phát sinh bằng TIỀN MẶT: {additionalFee:N0} VNĐ.",
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
                    decimal additionalAmount = totalAmount - depositAmount; 

                    _logger.LogInformation("Phát hiện hóa đơn đặt cọc trước cho Session {SessionId}. Số tiền cọc: {Deposit} VND. Tổng phí thực tế: {Total} VND. Cần thu thêm: {Additional} VND.",
                        session.SessionId, depositAmount, totalAmount, additionalAmount);

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
                    else
                    {
                        string txnRef = "INV" + DateTime.UtcNow.Ticks + "_" + depositAmount;
                        session.Invoice.PaymentStatus = "PENDING";
                        session.Invoice.TotalAmount = additionalAmount; 
                        session.Invoice.PaymentMethod = request.PaymentMethod.ToUpper();
                        session.Invoice.StaffId = currentStaffId;
                        session.Invoice.UpdatedDate = DateTime.UtcNow;
                        session.Invoice.TransactionCode = txnRef;

                        if (request.PaymentMethod.ToUpper() == "VNPAY")
                        {
                            _context.Invoices.Update(session.Invoice);
                            _context.ParkingSessions.Update(session);
                            await _context.SaveChangesAsync();
                            await dbTransaction.CommitAsync();

                            _logger.LogInformation("Tạo yêu cầu thanh toán VNPay số tiền chênh lệch sau cọc: {Amount} VNĐ cho Session {SessionId}. Mã Ref: {TxnRef}",
                                additionalAmount, session.SessionId, txnRef);

                            string paymentUrl = _vnPayService.CreatePaymentUrl(
                                txnRef: txnRef,
                                amount: additionalAmount,
                                orderInfo: $"Thanh toan phi phat sinh do xe phien {session.SessionId}",
                                returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + session.Invoice.InvoiceId,
                                ipAddress: "127.0.0.1"
                            );

                            return new CheckoutResponse
                            {
                                IsSuccess = true,
                                Message = $"Cần thanh toán thêm số tiền chênh lệch sau cọc: {additionalAmount:N0} VNĐ. Quét mã QR VNPay để thanh toán.",
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

                else if (session.Invoice != null && (session.Invoice.PaymentStatus == "PENDING" || session.Invoice.PaymentStatus == "FAILED"))
                {
                    var invoice = session.Invoice;
                    decimal depositAmount = 0;

                    if (!string.IsNullOrEmpty(invoice.TransactionCode) && invoice.TransactionCode.Contains("_"))
                    {
                        var parts = invoice.TransactionCode.Split('_');
                        if (parts.Length > 1 && decimal.TryParse(parts[1], out decimal parsedDeposit))
                        {
                            depositAmount = parsedDeposit;
                        }
                    }

                    decimal actualAmountToPay = totalAmount - depositAmount;
                    if (actualAmountToPay <= 0) actualAmountToPay = 0;

                    string txnRef = "INV" + DateTime.UtcNow.Ticks + (depositAmount > 0 ? "_" + depositAmount : "");

                    invoice.TotalAmount = actualAmountToPay;
                    invoice.PaymentMethod = request.PaymentMethod.ToUpper();
                    invoice.PaymentStatus = "PENDING";
                    invoice.StaffId = currentStaffId;
                    invoice.UpdatedDate = DateTime.UtcNow;
                    invoice.TransactionCode = txnRef;

                    if (request.PaymentMethod.ToUpper() == "VNPAY")
                    {
                        _context.Invoices.Update(invoice);
                        _context.ParkingSessions.Update(session);
                        await _context.SaveChangesAsync();
                        await dbTransaction.CommitAsync();

                        _logger.LogInformation("Kịch bản 2.3 - Cập nhật yêu cầu thanh toán VNPay (hóa đơn {InvoiceId}) số tiền chênh lệch sau cọc: {Amount} VNĐ cho Session {SessionId}. Mã Ref: {TxnRef}",
                            invoice.InvoiceId, actualAmountToPay, session.SessionId, txnRef);

                        string paymentUrl = _vnPayService.CreatePaymentUrl(
                            txnRef: txnRef,
                            amount: actualAmountToPay,
                            orderInfo: $"Thanh toan phi do xe phien {session.SessionId}",
                            returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                            ipAddress: "127.0.0.1"
                        );

                        return new CheckoutResponse
                        {
                            IsSuccess = true,
                            Message = $"Cập nhật VNPay thành công. Số tiền chênh lệch sau cọc: {actualAmountToPay:N0} VNĐ. Quét mã QR để thanh toán.",
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
                            TotalAmount = actualAmountToPay,
                            StaffName = staffName,
                            InvoiceId = invoice.InvoiceId,
                            IsPaid = false,
                            PaymentUrl = paymentUrl
                        };
                    }
                    else
                    {
                        _context.Invoices.Update(invoice);
                        _context.ParkingSessions.Update(session);
                        await _context.SaveChangesAsync();
                        await dbTransaction.CommitAsync();

                        _logger.LogInformation("Kịch bản 2.3 - Cập nhật yêu cầu thanh toán Tiền mặt (hóa đơn {InvoiceId}) số tiền chênh lệch sau cọc: {Amount} VNĐ cho Session {SessionId}.",
                            invoice.InvoiceId, actualAmountToPay, session.SessionId);

                        return new CheckoutResponse
                        {
                            IsSuccess = true,
                            Message = $"Cập nhật Tiền mặt thành công. Số tiền cần thu: {actualAmountToPay:N0} VNĐ.",
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
                            TotalAmount = actualAmountToPay,
                            StaffName = staffName,
                            InvoiceId = invoice.InvoiceId,
                            IsPaid = false,
                            PaymentUrl = null
                        };
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

                    string paymentUrl = _vnPayService.CreatePaymentUrl(
                        txnRef: txnRef,
                        amount: totalAmount,
                        orderInfo: $"Thanh toan phi do xe phien {session.SessionId}",
                        returnUrl: _vnPayConfig.ReturnUrl + "?invoiceId=" + invoice.InvoiceId,
                        ipAddress: "127.0.0.1"
                    );

                    return new CheckoutResponse
                    {
                        IsSuccess = true,
                        Message = "Vui lòng quét mã QR VNPay để thanh toán.",
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
                await dbTransaction.RollbackAsync();
                _logger.LogError(ex, "Lỗi hệ thống khi xử lý check-out cho Ticket {TicketCode}", cleanTicketCode);
                throw;
            }
        }
    }
}
