using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.API.Controllers
{
    [Authorize(Roles = "Admin,Manager")]
    [ApiController]
    [Route("api/[controller]")]
    public class ManagerController : ControllerBase
    {
        private readonly IManagerService _managerService;
        private readonly ILogger<ManagerController> _logger;

        public ManagerController(IManagerService managerService, ILogger<ManagerController> logger)
        {
            _managerService = managerService;
            _logger = logger;
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
                var stats = await _managerService.GetTrafficStatisticsAsync(request);
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
        /// Cấu hình biểu phí đỗ xe theo giờ (Chỉ áp dụng cho Manager/Admin).
        /// </summary>
        [HttpPut("update-pricing")]
        public async Task<IActionResult> UpdatePricing([FromBody] UpdateVehiclePriceRequest request)
        {
            _logger.LogInformation("Manager requested to update price for VehicleTypeId={TypeId} to {Price} VND",
                request.VehicleTypeId, request.NewPrice);

            if (request.NewPrice < 0)
            {
                return BadRequest("Giá cấu hình không được nhỏ hơn 0.");
            }

            try
            {
                var isSuccess = await _managerService.UpdateVehicleTypePriceAsync(request.VehicleTypeId, request.NewPrice);
                if (!isSuccess)
                {
                    _logger.LogWarning("Failed to update pricing: VehicleTypeId={TypeId} not found", request.VehicleTypeId);
                    return NotFound("Không tìm thấy loại xe yêu cầu.");
                }

                _logger.LogInformation("Successfully updated pricing for VehicleTypeId={TypeId}", request.VehicleTypeId);
                return Ok(new { isSuccess = true, message = "Cập nhật biểu phí đỗ xe thành công!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi quản lý cập nhật giá.");
                return StatusCode(500, "Lỗi máy chủ khi cập nhật.");
            }
        }
    }
}
