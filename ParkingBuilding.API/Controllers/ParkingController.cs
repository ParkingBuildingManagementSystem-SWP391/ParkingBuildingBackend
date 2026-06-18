using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System.Security.Claims;


namespace ParkingBuilding.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// API Controller quản lý các hoạt động đỗ xe.
    /// Cho phép khách đặt trước vị trí, nhân viên thực hiện check-in cho xe đã đặt hoặc xe vãng lai, và soát vé check-out đầu ra.
    /// </summary>
    public class ParkingController : ControllerBase
    {
        private readonly IBookingService _bookingService;
        private readonly ICheckInService _checkInService;
        private readonly ICheckOutService _checkOutService;
        private readonly IParkingQueryService _parkingQueryService;

        public ParkingController(
            IBookingService bookingService,
            ICheckInService checkInService,
            ICheckOutService checkOutService,
            IParkingQueryService parkingQueryService)
        {
            _bookingService = bookingService;
            _checkInService = checkInService;
            _checkOutService = checkOutService;
            _parkingQueryService = parkingQueryService;
        }

        // API 1: Khách đặt chỗ trước 
        /// <summary>
        /// API đặt chỗ trước dành cho tài xế thành viên.
        /// - BẢO MẬT: Trích xuất UserId trực tiếp từ JWT Token để đảm bảo định danh chính chủ.
        /// - Nghiệp vụ: Cấp phát tạm thời 1 ô đỗ trống và tạo vé giữ chỗ trong 15 phút.
        /// </summary>
        [Authorize(Roles = "Registered_Driver")]
        [HttpPost("book")]
        public async Task<IActionResult> BookSlot([FromBody] BookSlotRequest request)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin User trong Token." });
                }

                int userId = int.Parse(userIdClaim);

                BookSlotResponse response = await _bookingService.BookSlotAsync(userId, request);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        // API 2: QUÉT CỔNG VÀO CHECK-IN
        /// <summary>
        /// API check-in tại cổng vào dành cho xe đã đặt chỗ trước.
        /// - Yêu cầu vai trò Nhân viên (Staff).
        /// - Cập nhật trạng thái đỗ xe sang đang đỗ (InProgress).
        /// </summary>
        [Authorize(Roles = "Staff")] 
        [HttpPost("check-in")]
        public async Task<IActionResult> CheckInVehicle([FromBody] CheckInRequest request)
        {
            try
            {
                var isSuccess = await _checkInService.CheckInVehicleAsync(request);

                if (isSuccess)
                    return Ok(new { message = "Check-in thành công! Mời xe tiến qua thanh chắn vào bãi." });

                return BadRequest("Check-in thất bại. Không tìm thấy lịch trình đặt chỗ của xe này, hoặc đơn đặt chỗ đã quá hạn 15 phút nên hệ thống tự động hủy.");
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // API 3: Khách vãng lai đến cổng (Walk-in)
        /// <summary>
        /// API check-in tại cổng dành cho khách vãng lai (không đặt trước).
        /// - Yêu cầu vai trò Nhân viên (Staff).
        /// - Sử dụng cơ chế khóa Database chống tranh chấp để tự động tìm và gán 1 slot đỗ trống lập tức.
        /// </summary>
        [Authorize(Roles = "Staff")]
        [HttpPost("walk-in")]
        public async Task<IActionResult> WalkInCheckIn([FromBody] WalkInRequest request)
        {
            try
            {
                var result = await _checkInService.WalkInCheckInAsync(request);

                if (result.Status == "Error" || result.Status == "Full")
                {
                    return BadRequest(new { isSuccess = false, message = result.TicketCode });
                }

                return Ok(new
                {
                    isSuccess = true,
                    message = $"Check-in khách hàng thành công! Xe đỗ tại vị trí: {result.SlotName}.",
                    data = result
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, error = ex.Message });
            }
        }


        // API 4: Xác thực xe ra bãi & Tính tiền (Chưa cho xe ra bãi) 
        /// <summary>
        /// API quét xe ra tại cổng check-out (chưa mở cổng).
        /// - BẢO MẬT: Lấy StaffId từ JWT Token của nhân viên soát vé thực hiện.
        /// - Nghiệp vụ: Đối khớp biển số xe, tính tổng thời gian đỗ, áp dụng Grace Period (ân hạn 15 phút) nếu đã trả trước,
        ///   hoặc sinh yêu cầu thanh toán (CASH / VNPAY) nếu chưa trả đủ tiền.
        /// </summary>
        [Authorize(Roles = "Staff")]
        [HttpPost("check-out")]                         
        public async Task<IActionResult> CheckOutVehicle([FromBody] CheckoutRequest request)
        {
            try
            {
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin Staff thực hiện." });

                int currentStaffId = int.Parse(staffIdClaim);

                CheckoutResponse response = await _checkOutService.CheckoutVehicleAsync(request, currentStaffId);
                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }


        [HttpGet("floor/{floorId}")]
        public async Task<IActionResult> GetSlotsByFloorId(int floorId)
        {
            try
            {
                var slots = await _parkingQueryService.GetSlotsByFloorIdAsync(floorId);
                return Ok(slots);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        // Thêm endpoint này vào class ParkingController
        [Authorize(Roles = "Registered_Driver")]
        [HttpGet("my-bookings")]
        public async Task<IActionResult> GetMyBookings()
        {
            try
            {
                // Trích xuất UserId trực tiếp từ JWT Token của tài xế đang đăng nhập
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized(new
                    {
                        isSuccess = false,
                        message = "Không tìm thấy thông tin User trong Token."
                    });
                }

                int userId = int.Parse(userIdClaim);

                var response = await _parkingQueryService.GetMyBookingsAsync(userId);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    isSuccess = false,
                    message = ex.Message
                });
            }
        }

    }
}
