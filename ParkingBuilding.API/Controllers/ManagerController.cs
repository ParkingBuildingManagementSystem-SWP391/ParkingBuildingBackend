using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [Authorize(Roles = "Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class ManagerController : ControllerBase
    {
        private readonly IManagerService _managerService;
        private readonly ILogger<ManagerController> _logger;
        private readonly IStaffLogService _staffLogService;

        public ManagerController(IManagerService managerService, ILogger<ManagerController> logger, IStaffLogService staffLogService)
        {
            _managerService = managerService;
            _logger = logger;
            _staffLogService = staffLogService;
        }

        /// <summary>
        /// Lấy thông tin tổng hợp thời gian thực hiển thị trên Dashboard.
        /// </summary>
        [HttpGet("dashboard-summary")]
        public async Task<IActionResult> GetDashboardSummary()
        {
            _logger.LogInformation("Manager requested dashboard summary.");
            try
            {
                var summary = await _managerService.GetDashboardSummaryAsync();
                _logger.LogInformation("Successfully retrieved dashboard summary.");
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching dashboard summary.");
                return StatusCode(500, "Internal server error.");
            }
        }

        /// <summary>
        /// Truy vấn thống kê lưu lượng xe ra vào và doanh thu theo chu kỳ thời gian.
        /// </summary>
        [HttpGet("traffic-statistics")]
        public async Task<IActionResult> GetTrafficStatistics([FromQuery] TrafficStatsRequest request)
        {
            _logger.LogInformation("Querying traffic statistics: StartDate={Start}, EndDate={End}, GroupBy={GroupBy}",
                request.StartDate, request.EndDate, request.GroupBy);

            if (request.StartDate > request.EndDate)
            {
                _logger.LogWarning("Invalid date range: StartDate={Start} is after EndDate={End}", request.StartDate, request.EndDate);
                return BadRequest("Thời gian bắt đầu không được lớn hơn thời gian kết thúc.");
            }

            try
            {
                var stats = await _managerService.GetTrafficStatsticsAsync(request);
                _logger.LogInformation("Successfully retrieved traffic statistics with {Count} records.", stats.Count);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while querying traffic statistics.");
                return StatusCode(500, "Internal server error.");
            }
        }

        /// <summary>
        /// Tải xuống báo cáo thống kê dưới dạng file Excel hoặc PDF.
        /// </summary>
        [HttpGet("export-report")]
        public async Task<IActionResult> ExportReport([FromQuery] ReportExportRequest request)
        {
            _logger.LogInformation("Request to export report: Format={Format}, StartDate={Start}, EndDate={End}",
                request.Format, request.StartDate, request.EndDate);

            if (request.StartDate > request.EndDate)
            {
                _logger.LogWarning("Invalid date range for report export: StartDate={Start} is after EndDate={End}", request.StartDate, request.EndDate);
                return BadRequest("Thời gian bắt đầu không được lớn hơn thời gian kết thúc.");
            }

            try
            {
                var report = await _managerService.ExportReportAsync(request);
                _logger.LogInformation("Report successfully generated: FileName={FileName}, Size={Size} bytes",
                    report.FileName, report.FileBytes.Length);
                return File(report.FileBytes, report.ContentType, report.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while exporting report.");
                return StatusCode(500, "Internal server error.");
            }
        }

        /// <summary>
        /// Lấy chi tiết thông tin ô đỗ xe và thông tin khách hàng đặt/đang đỗ.
        /// </summary>
        [HttpGet("slot-detail/{slotId}")]
        public async Task<IActionResult> GetSlotDetail(int slotId)
        {
            _logger.LogInformation("Request detail for SlotId={SlotId}", slotId);
            try
            {
                var detail = await _managerService.GetSlotDetailAsync(slotId);
                if (detail == null)
                {
                    _logger.LogWarning("Slot detail not found for SlotId={SlotId}", slotId);
                    return NotFound("Không tìm thấy thông tin ô đỗ xe.");
                }
                _logger.LogInformation("Successfully retrieved slot detail for SlotId={SlotId}. Status={Status}", slotId, detail.SlotStatus);
                return Ok(detail);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching slot detail for SlotId={SlotId}", slotId);
                return StatusCode(500, "Internal server error.");
            }
        }

        /// <summary>
        /// Cấu hình biểu phí đỗ xe theo ca/lượt (Chỉ áp dụng cho Manager/Admin).
        /// </summary>
        [HttpPut("update-pricing")]
        public async Task<IActionResult> UpdatePricing([FromBody] UpdateVehiclePriceRequest request)
        {
            _logger.LogInformation("Manager requested to update pricing for VehicleTypeId={TypeId}", request.VehicleTypeId);

            if (request.DayRate < 0 || request.NightRate < 0 || request.FullDayRate < 0 || 
                request.MonthlyPrice < 0 || request.FirstHourRate < 0 || request.SubsequentHourRate < 0)
            {
                return BadRequest("Giá cấu hình không được nhỏ hơn 0.");
            }

            try
            {
                var isSuccess = await _managerService.UpdateVehicleTypePricingAsync(
                    request.VehicleTypeId,
                    request.DayRate,
                    request.NightRate,
                    request.FullDayRate,
                    request.MonthlyPrice,
                    request.FirstHourRate,
                    request.SubsequentHourRate); 

                if (!isSuccess)
                {
                    return NotFound("Không tìm thấy loại xe yêu cầu.");
                }

                return Ok(new { isSuccess = true, message = "Cập nhật biểu phí đỗ xe thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quản lý cập nhật giá.");
                return StatusCode(500, "Lỗi máy chủ khi cập nhật.");
            }
        }



        /// <summary>
        /// Cấu hình giá cho gói cước thành viên (Chỉ áp dụng cho Manager).
        /// </summary>
        [HttpPut("update-membership-pricing")]
        public async Task<IActionResult> UpdateMembershipPricing([FromBody] UpdateMembershipTierPriceRequest request)
        {
            _logger.LogInformation("Manager requested to update membership pricing for VehicleTypeId={TypeId}, DurationMonths={Duration}", 
                request.TypeId, request.DurationMonths);

            try
            {
                var result = await _managerService.UpdateMembershipTierPricingAsync(request);
                if (result == null)
                {
                    return NotFound("Không tìm thấy gói cước thành viên phù hợp với loại xe và số tháng yêu cầu.");
                }

                return Ok(new { isSuccess = true, data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quản lý cập nhật giá gói cước thành viên.");
                return StatusCode(500, "Lỗi máy chủ khi cập nhật: " + ex.Message);
            }
        }
        /// <summary>
        /// Lay danh sach the thanh vien de Manager quan ly.
        /// </summary>
        [HttpGet("membership-cards")]
        public async Task<IActionResult> GetMembershipCards([FromQuery] string? status, [FromQuery] string? search)
        {
            try
            {
                var cards = await _managerService.GetMembershipCardsAsync(status, search);
                return Ok(new { isSuccess = true, data = cards });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching membership cards.");
                return StatusCode(500, new { isSuccess = false, message = "Internal server error." });
            }
        }

        /// <summary>
        /// Huy the thanh vien boi Manager.
        /// </summary>
        [HttpDelete("membership-cards/{cardId}/cancel")]
        public async Task<IActionResult> CancelMembershipCard(int cardId)
        {
            try
            {
                var result = await _managerService.CancelMembershipCardByManagerAsync(cardId);
                if (!result.IsSuccess)
                {
                    return Conflict(new { isSuccess = false, message = result.Message });
                }

                return Ok(new { isSuccess = true, message = result.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while cancelling membership card {CardId}.", cardId);
                return StatusCode(500, new { isSuccess = false, message = "Internal server error." });
            }
        }

        /// <summary>
        /// Lấy danh sách ca trực của nhân viên để đối soát tiền mặt.
        /// </summary>
        [HttpGet("shifts")]
        public async Task<IActionResult> GetStaffShifts([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate, [FromQuery] string? status)
        {
            try
            {
                var shifts = await _staffLogService.GetShiftsForManagerAsync(startDate, endDate, status);
                return Ok(new { isSuccess = true, data = shifts });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching staff shifts for manager.");
                return StatusCode(500, new { isSuccess = false, message = "Internal server error." });
            }
        }

        /// <summary>
        /// Lấy nhật ký hoạt động chi tiết của toàn bộ nhân viên cổng.
        /// </summary>
        [HttpGet("staff-activities")]
        public async Task<IActionResult> GetStaffActivities([FromQuery] int? staffId, [FromQuery] string? actionType, [FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
        {
            try
            {
                var logs = await _staffLogService.GetActivityLogsForManagerAsync(staffId, actionType, startDate, endDate);
                return Ok(new { isSuccess = true, data = logs });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while fetching staff activity logs for manager.");
                return StatusCode(500, new { isSuccess = false, message = "Internal server error." });
            }
        }

        /// <summary>
        /// Khóa ô đỗ xe (Chỉ áp dụng cho Manager).
        /// </summary>
        [HttpPut("slots/{slotId}/lock")]
        public async Task<IActionResult> LockSlot(int slotId)
        {
            _logger.LogInformation("Manager requested to lock slot {SlotId}", slotId);
            try
            {
                var isSuccess = await _managerService.LockParkingSlotAsync(slotId);
                if (!isSuccess)
                {
                    return BadRequest("Không thể khóa ô đỗ xe này (Có thể ô đỗ không ở trạng thái trống hoặc không tồn tại).");
                }
                return Ok(new { isSuccess = true, message = "Khóa ô đỗ xe thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi khóa ô đỗ xe {SlotId}", slotId);
                return StatusCode(500, "Lỗi máy chủ khi khóa ô đỗ xe.");
            }
        }

        /// <summary>
        /// Mở khóa ô đỗ xe (Chỉ áp dụng cho Manager).
        /// </summary>
        [HttpPut("slots/{slotId}/unlock")]
        public async Task<IActionResult> UnlockSlot(int slotId)
        {
            _logger.LogInformation("Manager requested to unlock slot {SlotId}", slotId);
            try
            {
                var isSuccess = await _managerService.UnlockParkingSlotAsync(slotId);
                if (!isSuccess)
                {
                    return BadRequest("Không thể mở khóa ô đỗ xe này (Có thể ô đỗ không ở trạng thái khóa hoặc không tồn tại).");
                }
                return Ok(new { isSuccess = true, message = "Mở khóa ô đỗ xe thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi mở khóa ô đỗ xe {SlotId}", slotId);
                return StatusCode(500, "Lỗi máy chủ khi mở khóa ô đỗ xe.");
            }
        }
    }
}
