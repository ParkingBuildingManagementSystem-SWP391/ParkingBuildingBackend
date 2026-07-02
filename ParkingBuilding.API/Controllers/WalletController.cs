using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.IService;
using System.Security.Claims;
using System.Threading.Tasks;
using System;

namespace ParkingBuilding.API.Controllers
{
    [Authorize(Roles = "Registered_Driver")]
    [ApiController]
    [Route("api/[controller]")]
    public class WalletController : ControllerBase
    {
        private readonly IWalletService _walletService;

        public WalletController(IWalletService walletService)
        {
            _walletService = walletService;
        }

        [HttpGet("balance")]
        public async Task<IActionResult> GetBalance()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế.");

            int userId = int.Parse(userIdClaim);
            var balance = await _walletService.GetBalanceAsync(userId);
            return Ok(new { isSuccess = true, balance });
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế.");

            int userId = int.Parse(userIdClaim);
            var history = await _walletService.GetHistoryAsync(userId);
            return Ok(new { isSuccess = true, data = history });
        }

        [HttpPost("deposit")]
        public async Task<IActionResult> Deposit([FromBody] DepositRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế.");

            if (request.Amount <= 0)
                return BadRequest("Số tiền nạp phải lớn hơn 0 VNĐ.");

            int userId = int.Parse(userIdClaim);
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            try
            {
                var paymentUrl = await _walletService.CreateDepositUrlAsync(userId, request.Amount, ipAddress);
                return Ok(new { isSuccess = true, paymentUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }
    }

    public class DepositRequest
    {
        public decimal Amount { get; set; }
    }
}
