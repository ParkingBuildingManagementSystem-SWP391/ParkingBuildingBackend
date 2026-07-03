using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Collections.Generic;
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
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            string ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            try
            {
                var result = await _membershipCardService.RegisterMembershipCardAsync(currentUserId, request, ipAddress);
                return Ok(new { isSuccess = true, data = result });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { isSuccess = false, message = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { isSuccess = false, message = ex.Message }); // 409
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
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var cards = await _membershipCardService.GetMyActiveCardsAsync(currentUserId);
                if (cards == null || cards.Count == 0)
                    return NotFound(new { isSuccess = false, message = "Bạn chưa đăng ký thẻ thành viên hoặc thẻ đã hết hạn." });

                return Ok(new { isSuccess = true, cards });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi hệ thống: " + ex.Message);
            }
        }

        /// <summary>
        /// Lấy danh sách thẻ thành viên đang hoạt động của tài xế đang đăng nhập.
        /// </summary>
        [HttpGet("my-active-cards")]
        public async Task<IActionResult> GetMyActiveCards()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var cards = await _membershipCardService.GetMyActiveCardsAsync(currentUserId);
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

        /// <summary>
        /// Hủy thẻ thành viên đang hoạt động (DELETE).
        /// </summary>
        [HttpDelete("{cardId}/cancel")]
        public async Task<IActionResult> CancelMembership(int cardId)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var success = await _membershipCardService.CancelMembershipCardAsync(cardId, currentUserId);
                return success
                    ? Ok(new { isSuccess = true, message = "Đã hủy thẻ thành viên thành công." })
                    : NotFound(new { isSuccess = false, message = "Không tìm thấy thẻ hoặc không có quyền hủy." });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        /// <summary>
        /// Hủy thẻ thành viên đang hoạt động (POST).
        /// </summary>
        [HttpPost("cancel")]
        public async Task<IActionResult> CancelMembershipPost([FromBody] CancelMembershipCardRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var success = await _membershipCardService.CancelMembershipCardAsync(request.CardId, currentUserId);
                return success
                    ? Ok(new { isSuccess = true, message = "Đã hủy thẻ thành viên thành công." })
                    : NotFound(new { isSuccess = false, message = "Không tìm thấy thẻ hoặc không có quyền hủy." });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { isSuccess = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        /// <summary>
        /// Cập nhật danh sách biển số xe trên thẻ thành viên (phương thức cũ dùng tham số route).
        /// </summary>
        [HttpPut("{cardId}/vehicles")]
        public async Task<IActionResult> UpdateVehicles(int cardId, [FromBody] List<string> plates)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var success = await _membershipCardService.UpdateMembershipVehiclesAsync(cardId, currentUserId, plates);
                return success
                    ? Ok(new { isSuccess = true, message = "Đã cập nhật biển số xe thành công." })
                    : NotFound(new { isSuccess = false, message = "Không tìm thấy thẻ hoặc không có quyền cập nhật." });
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
        /// Cập nhật danh sách biển số xe trên thẻ thành viên (PUT /api/MembershipCard/update-vehicles).
        /// </summary>
        [HttpPut("update-vehicles")]
        public async Task<IActionResult> UpdateVehiclesPost([FromBody] UpdateMembershipVehiclesRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được danh tính tài xế từ Token.");

            int currentUserId = int.Parse(userIdClaim);
            try
            {
                var success = await _membershipCardService.UpdateMembershipVehiclesAsync(request.CardId, currentUserId, request.LicenseVehicles);
                return success
                    ? Ok(new { isSuccess = true, message = "Đã cập nhật biển số xe thành công." })
                    : NotFound(new { isSuccess = false, message = "Không tìm thấy thẻ hoặc không có quyền cập nhật." });
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
    }
}
