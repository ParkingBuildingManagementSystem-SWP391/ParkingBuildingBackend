using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging; // Thêm namespace logging
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service;
using ParkingBuilding.Service.Service.Helpers;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{

    [Authorize] 
    [ApiController]
    [Route("api/[controller]")]
    /// <summary>
    /// API CONTROLLER: Quản lý các hoạt động thanh toán hóa đơn đỗ xe.
    /// - CHỨC NĂNG CHÍNH: Xử lý thanh toán tiền mặt tại quầy, khởi tạo mã VNPay QR, thanh toán bằng Ví điện tử, và nhận Webhook IPN từ VNPay.
    /// - ĐẦU VÀO (Input): HTTP Post/Get Request (JSON Body / Query Param) từ FE Staff, FE Tài xế hoặc Cổng thanh toán VNPay.
    /// - ĐẦU RA (Output): Điều hướng gọi `IPaymentService` -> Trả về kết quả giao dịch `PaymentResultDto` hoặc HTTP Response.
    /// </summary>
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly VnPayConfig _vnPayConfig;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            IPaymentService paymentService, 
            IOptions<VnPayConfig> vnPayConfig, 
            ILogger<PaymentsController> logger)
        {
            _paymentService = paymentService;
            _vnPayConfig = vnPayConfig.Value;
            _logger = logger;
        }

        /// <summary>
        /// API Ghi nhận thanh toán tiền mặt tại quầy (Staff).
        /// - CHỨC NĂNG: Thu ngân xác nhận thu tiền mặt cho lượt đỗ xe.
        /// - ĐẦU VÀO: Body `CashPaymentDto request` (Mã vé `TicketCode`, số tiền nhận), JWT Token (StaffId).
        /// - ĐẦU RA: Gọi `_paymentService.ProcessCashPaymentAsync` -> Trả về `PaymentResultDto`.
        /// </summary>
        [Authorize(Roles = "Staff")]
        [HttpPost("cash")]
        public async Task<IActionResult> ProcessCashPayment([FromBody] CashPaymentDto request)
        {
            var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(staffIdClaim))
            {
                _logger.LogWarning("Thanh toán tiền mặt thất bại: Không thể xác định mã nhân viên từ token.");
                return Unauthorized("Không tìm thấy thông tin định danh nhân viên trong Token.");
            }

            int currentStaffId = int.Parse(staffIdClaim);
            _logger.LogInformation("Nhân viên {StaffId} bắt đầu xác nhận thanh toán tiền mặt cho vé {TicketCode}.", currentStaffId, request.TicketCode);

            var result = await _paymentService.ProcessCashPaymentAsync(request, currentStaffId);
            if (!result.Success)
                {
                _logger.LogWarning("Thanh toán tiền mặt thất bại cho vé {TicketCode}: {Message}", request.TicketCode, result.Message);
                    return BadRequest(result.Message);
                }

            return Ok(result);
        }

        /// <summary>
        /// API Tài xế tạo mã QR / Liên kết thanh toán VNPay.
        /// - CHỨC NĂNG: Khởi tạo URL chuyển hướng đến cổng thanh toán VNPay.
        /// - ĐẦU VÀO: Body `CreateVnPayPaymentDto request` (SessionId/InvoiceId), JWT Token (UserId).
        /// - ĐẦU RA: Gọi `_paymentService.CreateVnPayPaymentUrlAsync` -> Trả về `PaymentResultDto` (URL thanh toán VNPay).
        /// </summary>
        [HttpPost("vnpay/create")]
        public async Task<IActionResult> CreateVnPayPayment([FromBody] CreateVnPayPaymentDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim)) 
            {
                _logger.LogWarning("Yêu cầu tạo link VNPay thất bại: Không thể định danh người dùng.");
                return Unauthorized("Không tìm thấy thông tin tài xế.");
            }
            int currentUserId = int.Parse(userIdClaim);

            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            request.IpAddress = ipAddress;

            _logger.LogInformation("Tài xế {UserId} yêu cầu tạo link VNPay cho phiên {SessionId} từ IP {IP}.", currentUserId, request.SessionId, ipAddress);

            var result = await _paymentService.CreateVnPayPaymentUrlAsync(request, _vnPayConfig, currentUserId);
            if (!result.Success) 
            {
                _logger.LogWarning("Tạo link VNPay thất bại cho phiên {SessionId}: {Message}", request.SessionId, result.Message);
                return BadRequest(result.Message);
            }

            return Ok(result);
        }

        /// <summary>
        /// API Thanh toán hóa đơn bằng Ví điện tử cá nhân.
        /// - CHỨC NĂNG: Trừ số dư trong ví người dùng để thanh toán hóa đơn đỗ xe.
        /// - ĐẦU VÀO: Body `PayPendingInvoiceWalletRequest request` (InvoiceId), JWT Token (UserId).
        /// - ĐẦU RA: Gọi `_paymentService.PayPendingInvoiceByWalletAsync` -> Trả về `PaymentResultDto`.
        /// </summary>
        [HttpPost("pay-pending-invoice-wallet")]
        public async Task<IActionResult> PayPendingInvoiceByWallet([FromBody] PayPendingInvoiceWalletRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized("Khong tim thay thong tin nguoi dung.");
            }

            int currentUserId = int.Parse(userIdClaim);
            var result = await _paymentService.PayPendingInvoiceByWalletAsync(request.InvoiceId, currentUserId);

            if (!result.Success)
            {
                return BadRequest(result.Message);
            }

            return Ok(result);
        }


        /// <summary>
        /// Webhook IPN Callback nhận phản hồi trạng thái giao dịch tự động từ VNPay.
        /// - CHỨC NĂNG: Xác thực chữ ký Hash (Checksum) và cập nhật trạng thái thanh toán tự động trong DB.
        /// - ĐẦU VÀO: Query Param `VnPayIpnRequestDto request` tự động gửi từ máy chủ VNPay.
        /// - ĐẦU RA: Gọi `_paymentService.ConfirmVnPayPaymentAsync` -> Trả về JSON theo đúng chuẩn VNPay IPN Protocol (`RspCode`, `Message`).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("vnpay-ipn")]
        public async Task<IActionResult> VnPayIpn([FromQuery] VnPayIpnRequestDto request)
        {
            try
            {
                _logger.LogInformation("Nhận webhook IPN từ VNPay. Mã Ref: {TxnRef}, Số tiền: {Amount}, Mã phản hồi: {ResponseCode}", 
                    request.vnp_TxnRef, request.vnp_Amount, request.vnp_ResponseCode);

                var vnpay = new VnPayLibrary();

                // 1. Duyệt qua tất cả các tham số query thực tế từ Request để nạp dữ liệu chữ ký (Đảm bảo an toàn khi VNPay thêm param mới)
                foreach (var key in Request.Query.Keys)
                {
                    if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_") && key != "vnp_SecureHash")
                    {
                        string val = Request.Query[key].ToString();
                        vnpay.AddResponseData(key, val);
                    }
                }

                // 2. Xác thực chữ ký bảo mật bảo vệ chống tin tặc giả mạo dữ liệu (Hỗ trợ bypass nếu truyền chữ ký là "debug" để test Swagger)
                string secretKey = _vnPayConfig.HashSecret ?? "";
                bool isValidSignature = (request.vnp_SecureHash == "debug") || vnpay.ValidateSignature(request.vnp_SecureHash ?? "", secretKey);
                if (!isValidSignature)
                {
                    _logger.LogWarning("VNPay IPN: Chữ ký Hash không hợp lệ cho giao dịch {TxnRef}.", request.vnp_TxnRef);
                    return Ok(new { RspCode = "97", Message = "Invalid signature" });
                }

                // 3. Đọc dữ liệu trực tiếp từ các trường của DTO
                string txnRef = request.vnp_TxnRef ?? "";
                string rawAmount = request.vnp_Amount ?? "0";
                decimal vnpayAmount = (decimal.TryParse(rawAmount, out decimal parsedAmount) ? parsedAmount : 0m) / 100m;
                string responseCode = request.vnp_ResponseCode ?? "";
                string transactionStatus = request.vnp_TransactionStatus ?? "";

                if (txnRef.StartsWith("WDEP_"))
                {
                    var walletService = HttpContext.RequestServices.GetService(typeof(IWalletService)) as IWalletService;
                    if (walletService == null)
                    {
                        return Ok(new { RspCode = "99", Message = "WalletService not initialized" });
                    }
                    var walletResult = await walletService.ConfirmDepositPaymentAsync(txnRef, vnpayAmount, responseCode, transactionStatus);
                    if (!walletResult.Success)
                    {
                        return Ok(new { RspCode = "99", Message = walletResult.Message });
                    }
                    return Ok(new { RspCode = "00", Message = "Confirm Success" });
                }

                if (txnRef.StartsWith("MBC_"))
                {
                    var membershipService = HttpContext.RequestServices.GetService(typeof(IMembershipCardService)) as IMembershipCardService;
                    if (membershipService == null)
                    {
                        return Ok(new { RspCode = "99", Message = "MembershipCardService not initialized" });
                    }
                    var mcResult = await membershipService.ConfirmMembershipCardPaymentAsync(txnRef, vnpayAmount, responseCode, transactionStatus);
                    if (!mcResult.Success)
                    {
                        return Ok(new { RspCode = mcResult.ErrorCode, Message = mcResult.Message });
                    }
                    return Ok(new { RspCode = "00", Message = "Confirm Success" });
                }

                // 4. Gọi Service cập nhật Database
                var result = await _paymentService.ConfirmVnPayPaymentAsync(txnRef, vnpayAmount, responseCode, transactionStatus);

                if (!result.Success)
                {
                    _logger.LogWarning("VNPay IPN: Ghi nhận thanh toán thất bại cho giao dịch {TxnRef}. ErrorCode: {ErrorCode}, Lỗi: {Message}", 
                        txnRef, result.ErrorCode, result.Message);
                    return Ok(new { RspCode = result.ErrorCode, Message = result.Message });
                }

                _logger.LogInformation("VNPay IPN: Ghi nhận thanh toán THÀNH CÔNG cho giao dịch {TxnRef}.", txnRef);
                return Ok(new { RspCode = "00", Message = "Confirm Success" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VNPay IPN: Lỗi hệ thống nghiêm trọng khi xử lý giao dịch {TxnRef}", request.vnp_TxnRef);
                return Ok(new { RspCode = "99", Message = "System Error: " + ex.Message });
            }
        }


        [HttpGet("status/{invoiceId}")]
        /// <summary>
        /// API kiểm tra trạng thái thanh toán hiện tại của hóa đơn (PENDING/SUCCESS/FAILED).
        /// </summary>
        public async Task<IActionResult> GetPaymentStatus(int invoiceId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(roleClaim))
                {
                    _logger.LogWarning("Truy vấn hóa đơn {InvoiceId} thất bại: Không xác định được thông tin người dùng từ token.", invoiceId);
                    return Unauthorized("Không thể xác định thông tin người dùng.");
                }

                int currentUserId = int.Parse(userIdClaim);
                string currentUserRole = roleClaim;

                _logger.LogInformation("Người dùng {UserId} (Vai trò: {Role}) truy vấn trạng thái hóa đơn {InvoiceId}.", currentUserId, currentUserRole, invoiceId);

                var status = await _paymentService.GetPaymentStatusAsync(invoiceId, currentUserId, currentUserRole);
                if (status == null)
                {
                    _logger.LogWarning("Không tìm thấy thông tin hóa đơn {InvoiceId}.", invoiceId);
                    return NotFound("Hóa đơn không tồn tại");
                }

                return Ok(new { status = status });
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Từ chối truy cập: Người dùng {UserId} không được phép xem hóa đơn {InvoiceId}.", User.FindFirst(ClaimTypes.NameIdentifier)?.Value, invoiceId);
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi truy vấn trạng thái hóa đơn {InvoiceId}.", invoiceId);
                return BadRequest(new { error = ex.Message });
            }
        }


    }

}
