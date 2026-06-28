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
        private readonly IImageStorageService _imageStorageService;
        private readonly IAiRecognitionService _aiRecognitionService;

        public ParkingController(
            IBookingService bookingService,
            ICheckInService checkInService,
            ICheckOutService checkOutService,
            IParkingQueryService parkingQueryService,
            IImageStorageService imageStorageService,
            IAiRecognitionService aiRecognitionService)
        {
            _bookingService = bookingService;
            _checkInService = checkInService;
            _checkOutService = checkOutService;
            _parkingQueryService = parkingQueryService;
            _imageStorageService = imageStorageService;
            _aiRecognitionService = aiRecognitionService;
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
        public async Task<IActionResult> CheckInVehicle([FromForm] CheckInRequest request)
        {
            try
            {
                var response = await _checkInService.CheckInVehicleAsync(request);

                if (response.IsSuccess)
                {
                    return Ok(new 
                    { 
                        isSuccess = true, 
                        message = response.Message, 
                        data = response 
                    });
                }

                return BadRequest(new { isSuccess = false, message = response.Message });
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
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
        public async Task<IActionResult> WalkInCheckIn([FromForm] WalkInRequest request)
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
        public async Task<IActionResult> CheckOutVehicle([FromForm] CheckoutRequest request)
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

        [HttpGet("slots")]
        public async Task<IActionResult> GetSlots([FromQuery] int? typeId, [FromQuery] string? status)
        {
            try
            {
                var slots = await _parkingQueryService.GetSlotsAsync(typeId, status);
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

        // API: Driver hủy đặt chỗ trước khi check-in
        [Authorize(Roles = "Registered_Driver")]
        [HttpPost("cancel-booking/{sessionId}")]
        public async Task<IActionResult> CancelBooking(int sessionId)
        {
            try
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin User trong Token." });
                }

                int userId = int.Parse(userIdClaim);
                var response = await _bookingService.CancelBookingAsync(userId, sessionId);

                if (!response.IsSuccess)
                {
                    return BadRequest(response);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpPost("recognize")]
        public async Task<IActionResult> RecognizePlate([FromForm] RecognizePlateRequest request)
        {
            try
            {
                if (request.ImageFile == null || request.ImageFile.Length == 0)
                    return BadRequest(new { isSuccess = false, message = "Vui lòng cung cấp file ảnh phương tiện." });

                // 1. Upload lên thư mục tạm của Cloudinary
                var uploadResult = await _imageStorageService.UploadImageDetailedAsync(request.ImageFile, "parking_temp");

                // 2. Gọi dịch vụ Python AI bằng URL gốc (RawUrl)
                if (request.VehicleTypeId == 1) // Xe đạp
                {
                    string bikePlate = $"BIKE_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                    return Ok(new
                    {
                        isSuccess = true,
                        imageUrl = uploadResult.OptimizedUrl,
                        rawImageUrl = uploadResult.RawUrl,
                        predictedPlate = bikePlate,
                        message = "Xe đạp: Tự động tạo mã định danh ảo thành công."
                    });
                }

                bool isMotorbike = (request.VehicleTypeId == 2);

                string detectedPlate = "";
                try
                {
                    detectedPlate = await _aiRecognitionService.PredictLicensePlateAsync(uploadResult.RawUrl);
                }
                catch (Exception aiEx)
                {
                    return Ok(new
                    {
                        isSuccess = true,
                        imageUrl = uploadResult.OptimizedUrl,
                        rawImageUrl = uploadResult.RawUrl,
                        predictedPlate = "",
                        message = $"Không thể nhận dạng tự động: {aiEx.Message}. Vui lòng nhập tay."
                    });
                }

                return Ok(new
                {
                    isSuccess = true,
                    imageUrl = uploadResult.OptimizedUrl,
                    rawImageUrl = uploadResult.RawUrl,
                    predictedPlate = detectedPlate
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("active-sessions")]
        public async Task<IActionResult> GetActiveSessions()
        {
            try
            {
                var sessions = await _parkingQueryService.GetActiveSessionsAsync();
                return Ok(sessions);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("scan-checkin/{ticketCode}")]
        public async Task<IActionResult> ScanCheckIn(string ticketCode, [FromQuery] string? detectedPlate)
        {
            try
            {
                var response = await _checkInService.ScanQrCheckInAsync(ticketCode, detectedPlate);
                if (!response.IsSuccess)
                    return BadRequest(response);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        [Authorize(Roles = "Staff")]
        [HttpGet("scan-checkout/{ticketCode}")]
        public async Task<IActionResult> ScanCheckOut(string ticketCode, [FromQuery] string? detectedPlate)
        {
            try
            {
                var response = await _checkOutService.ScanQrCheckOutAsync(ticketCode, detectedPlate);
                if (!response.IsSuccess)
                    return BadRequest(response);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        [AllowAnonymous]
        [HttpGet("locate")]
        public async Task<IActionResult> LocateVehicle([FromQuery] string licensePlate)
        {
            try
            {
                var result = await _parkingQueryService.LocateVehicleAsync(licensePlate);

                if (result == null)
                {
                    return NotFound(new
                    {
                        isSuccess = false,
                        message = "Không tìm thấy xe đang đỗ trong bãi với biển số này."
                    });
                }
                return Ok(new
                {
                    isSuccess = true,
                    data = result
                });
            }
            catch (ArgumentException argEx)
            {
                return BadRequest(new { isSuccess = false, message = argEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { isSuccess = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }


    }
}
