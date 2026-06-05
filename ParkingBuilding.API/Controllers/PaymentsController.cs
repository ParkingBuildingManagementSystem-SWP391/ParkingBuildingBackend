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

    [Authorize] // Yêu cầu xác thực JWT Token để truy cập các API thanh toán
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly VnPayConfig _vnPayConfig;

        public PaymentsController(IPaymentService paymentService, IOptions<VnPayConfig> vnPayConfig)
        {
            _paymentService = paymentService;
            _vnPayConfig = vnPayConfig.Value;
        }

        [HttpPost("cash")]
        public async Task<IActionResult> ProcessCashPayment([FromBody] CashPaymentDto request)
        {
            // Trích xuất StaffId an toàn từ Claims trong JWT Token gửi kèm trong Header
            var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                               ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(staffIdClaim))
            {
                return Unauthorized("Không tìm thấy thông tin định danh nhân viên trong Token.");
            }

            int currentStaffId = int.Parse(staffIdClaim);

            // Truyền staffId đã được xác thực an toàn xuống Service
            var result = await _paymentService.ProcessCashPaymentAsync(request, currentStaffId);
            if (!result.Success) return BadRequest(result.Message);

            return Ok(result);
        }


        [HttpPost("vnpay/create")]
        public async Task<IActionResult> CreateVnPayPayment([FromBody] CreateVnPayPaymentDto request)
        {
            // Lấy địa chỉ IP của Client gửi yêu cầu
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            request.IpAddress = ipAddress;

            var result = await _paymentService.CreateVnPayPaymentUrlAsync(request, _vnPayConfig);
            if (!result.Success) return BadRequest(result.Message);

            return Ok(result);
        }
        [HttpGet("vnpay-ipn")]
        public async Task<IActionResult> VnPayIpn()
        {
            var queryParams = Request.Query;
            var vnpay = new VnPayLibrary();
            string vnp_SecureHash = "";

            // 1. Trích xuất các tham số vnp_ nhận được
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

            // 2. Xác thực chữ ký số bằng Helper
            bool isValidSignature = vnpay.ValidateSignature(vnp_SecureHash, _vnPayConfig.HashSecret);
            if (!isValidSignature)
            {
                return Ok(new { RspCode = "97", Message = "Invalid signature" });
            }

            // 3. Đọc dữ liệu giao dịch từ VNPay trả về
            string txnRef = vnpay.GetResponseData("vnp_TxnRef");
            long vnpayAmount = Convert.ToInt64(vnpay.GetResponseData("vnp_Amount")) / 100;
            string responseCode = vnpay.GetResponseData("vnp_ResponseCode");

            // 4. Gọi service xử lý cập nhật trạng thái hóa đơn & phiên đỗ xe
            var result = await _paymentService.ConfirmVnPayPaymentAsync(txnRef, vnpayAmount, responseCode);

            if (!result.Success)
            {
                // Trả về mã lỗi tương ứng theo chuẩn quy định của VNPay
                return Ok(new { RspCode = result.ErrorCode, Message = result.Message });
            }

            // Phản hồi thành công về cho Server VNPay để kết thúc phiên IPN
            return Ok(new { RspCode = "00", Message = "Confirm Success" });
        }



        [HttpGet("status/{invoiceId}")]
        public async Task<IActionResult> GetPaymentStatus(int invoiceId)
        {
            var status = await _paymentService.GetPaymentStatusAsync(invoiceId);

            if (status == null)
                return NotFound("Hóa đơn không tồn tại");

            return Ok(new { status = status });
        }


    }

}