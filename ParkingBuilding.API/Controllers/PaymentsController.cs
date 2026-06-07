using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    /// API Controller quản lý các hoạt động thanh toán.
    /// Cho phép thanh toán bằng tiền mặt qua Staff, tạo link thanh toán VNPay và xử lý webhook IPN tự động từ VNPay.
    /// </summary>
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly VnPayConfig _vnPayConfig;

        public PaymentsController(IPaymentService paymentService, IOptions<VnPayConfig> vnPayConfig)
        {
            _paymentService = paymentService;
            _vnPayConfig = vnPayConfig.Value;
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("cash")]
        /// <summary>
        /// API ghi nhận thanh toán bằng tiền mặt.
        /// - BẢO MẬT: Lấy StaffId trực tiếp từ JWT Token của nhân viên xác nhận giao dịch để đảm bảo truy vết trách nhiệm.
        /// </summary>
        public async Task<IActionResult> ProcessCashPayment([FromBody] CashPaymentDto request)
        {
            var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(staffIdClaim))
            {
                return Unauthorized("Không tìm thấy thông tin định danh nhân viên trong Token.");
            }

            int currentStaffId = int.Parse(staffIdClaim);

            var result = await _paymentService.ProcessCashPaymentAsync(request, currentStaffId);
            if (!result.Success) return BadRequest(result.Message);

            return Ok(result);
        }


        [HttpPost("vnpay/create")]
        /// <summary>
        /// API tài xế chủ động tạo mã QR VNPay để thanh toán trước qua App di động.
        /// - BẢO MẬT: Trích xuất UserId từ JWT Token của tài xế đang đăng nhập.
        /// </summary>
        public async Task<IActionResult> CreateVnPayPayment([FromBody] CreateVnPayPaymentDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized("Không tìm thấy thông tin tài xế.");
            int currentUserId = int.Parse(userIdClaim);

            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            request.IpAddress = ipAddress;

            var result = await _paymentService.CreateVnPayPaymentUrlAsync(request, _vnPayConfig, currentUserId);
            if (!result.Success) return BadRequest(result.Message);

            return Ok(result);
        }


        [AllowAnonymous]
        [HttpGet("vnpay-ipn")]
        /// <summary>
        /// Webhook IPN Callback nhận phản hồi trạng thái giao dịch tự động từ VNPay.
        /// - BẢO MẬT: Sử dụng [AllowAnonymous] vì webhook được gọi tự động từ máy chủ VNPay mà không mang theo Token JWT.
        /// - Xác thực: So khớp chữ ký HMAC-SHA512 để tránh tin tặc giả mạo thông tin thanh toán thành công.
        /// </summary>
        public async Task<IActionResult> VnPayIpn()
        {

            try
            {
                var queryParams = Request.Query;
                var vnpay = new VnPayLibrary();
                string vnp_SecureHash = "";

                foreach (var key in queryParams.Keys)
                {
                    if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                    {
                        if (key == "vnp_SecureHash")
                        {
                            vnp_SecureHash = queryParams[key];
                        }
                        else
                        {
                            vnpay.AddResponseData(key, queryParams[key]);
                        }
                    }
                }

                bool isValidSignature = vnpay.ValidateSignature(vnp_SecureHash, _vnPayConfig.HashSecret);
                if (!isValidSignature)
                {
                    return Ok(new { RspCode = "97", Message = "Invalid signature" });
                }

                string txnRef = vnpay.GetResponseData("vnp_TxnRef");
                decimal vnpayAmount = Convert.ToDecimal(vnpay.GetResponseData("vnp_Amount")) / 100m;
                string responseCode = vnpay.GetResponseData("vnp_ResponseCode");

                var result = await _paymentService.ConfirmVnPayPaymentAsync(txnRef, vnpayAmount, responseCode);

                if (!result.Success)
                {
                    return Ok(new { RspCode = result.ErrorCode, Message = result.Message });
                }

                return Ok(new { RspCode = "00", Message = "Confirm Success" });
            }
            catch (Exception ex)
            {
                             
                return Ok(new { RspCode = "99", Message = "System Error: " + ex.Message });
            }
        }



        [HttpGet("status/{invoiceId}")]
        /// <summary>
        /// API kiểm tra trạng thái thanh toán hiện tại của hóa đơn (PENDING/SUCCESS/FAILED).
        /// - BẢO MẬT: Ràng buộc chặt chẽ, tài xế chỉ có quyền xem trạng thái hóa đơn của chính mình.
        /// </summary>
        public async Task<IActionResult> GetPaymentStatus(int invoiceId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;

                if (string.IsNullOrEmpty(userIdClaim) || string.IsNullOrEmpty(roleClaim))
                {
                    return Unauthorized("Không thể xác định thông tin người dùng.");
                }

                int currentUserId = int.Parse(userIdClaim);
                string currentUserRole = roleClaim;

                var status = await _paymentService.GetPaymentStatusAsync(invoiceId, currentUserId, currentUserRole);
                if (status == null)
                    return NotFound("Hóa đơn không tồn tại");

                return Ok(new { status = status });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }


    }

}