using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [Authorize(Roles = "Registered_Driver")]
    [ApiController]
    [Route("api/[controller]")]
    public class MembershipCardController : ControllerBase
    {
        private readonly IMembershipCardService _membershipCardService;

        public MembershipCardController(IMembershipCardService membershipCardService)
        {
            _membershipCardService = membershipCardService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterMembershipCardDto request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");
            }

            int currentUserId = int.Parse(userIdClaim);
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            try
            {
                var result = await _membershipCardService.RegisterMembershipCardAsync(currentUserId, request, ipAddress);
                return Ok(new { isSuccess = true, data = result });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        /// <summary>
        /// Lấy thông tin thẻ thành viên đang hoạt động của tài xế đang đăng nhập.
        /// </summary>
        [HttpGet("my-card")]
        public async Task<IActionResult> GetMyActiveCard()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
            {
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");
            }

            int currentUserId = int.Parse(userIdClaim);

            try
            {
                var cards = await _membershipCardService.GetMyActiveCardsAsync(currentUserId);
                if (cards == null || cards.Count == 0)
                {
                    return NotFound(new { isSuccess = false, message = "Bạn chưa đăng ký thẻ thành viên hoặc thẻ đã hết hạn." });
                }

                return Ok(new { isSuccess = true, cards });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi hệ thống: " + ex.Message);
            }
        }

        /// <summary>
        /// Lấy danh sách các gói cước thành viên đang hoạt động.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("tiers")]
        public async Task<IActionResult> GetTiers()
        {
            try
            {
                var tiers = await _membershipCardService.GetActiveTiersAsync();
                return Ok(new { isSuccess = true, data = tiers });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
    }
}
