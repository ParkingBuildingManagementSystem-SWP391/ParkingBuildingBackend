using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using System.Security.Claims;


namespace ParkingBuilding.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    /// <summary>
    /// API CONTROLLER: Cổng giao tiếp chính cho các hoạt động đỗ xe (Parking Workflow).
    /// - CHỨC NĂNG CHÍNH: Đặt chỗ trước (Booking), Check-in cổng vào, Check-out cổng ra, Quét QR soát vé, Nhận diện biển số AI.
    /// - ĐẦU VÀO (Input): Nhận các HTTP Request (Form Data / JSON Body / Path Param) từ Frontend (Mobile App Tài xế / Web Staff).
    /// - ĐẦU RA (Output): Điều hướng gọi các Service tương ứng (`CheckInService`, `CheckOutService`, `BookingService`) -> Trả HTTP Response (`200 OK`, `400 BadRequest`) về Frontend.
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
        /// - CHỨC NĂNG: Cấp phát tạm thời 1 ô đỗ trống và sinh mã vé giữ chỗ trong 15 phút.
        /// - ĐẦU VÀO: JWT Token (Chứa UserId), `BookSlotRequest request` (Loại xe, Biển số).
        /// - ĐẦU RA: Gọi `_bookingService.BookSlotAsync` -> Trả về `BookSlotResponse` (Mã vé `TicketCode`, Tên ô đỗ).
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
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin Staff thực hiện." });

                int currentStaffId = int.Parse(staffIdClaim);
                var response = await _checkInService.CheckInVehicleAsync(request, currentStaffId);

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
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin Staff thực hiện." });

                int currentStaffId = int.Parse(staffIdClaim);
                var result = await _checkInService.WalkInCheckInAsync(request, currentStaffId);

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


        /// <summary>
        /// API Lấy danh sách ô đỗ xe theo tầng.
        /// - CHỨC NĂNG: Truy vấn danh sách ô đỗ (Slots) thuộc tầng chỉ định.
        /// - ĐẦU VÀO: Path Param `{floorId}` (ID tầng).
        /// - ĐẦU RA: Gọi `_parkingQueryService.GetSlotsByFloorIdAsync` -> Trả về danh sách ô đỗ.
        /// </summary>
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

        /// <summary>
        /// API Lấy danh sách vị trí đỗ theo loại xe và trạng thái.
        /// - CHỨC NĂNG: Lọc danh sách ô đỗ theo loại xe (TypeId) và trạng thái (Status).
        /// - ĐẦU VÀO: Query Param `typeId`, `status`.
        /// - ĐẦU RA: Gọi `_parkingQueryService.GetSlotsAsync` -> Trả về danh sách ô đỗ đỗ thỏa điều kiện.
        /// </summary>
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
        /// <summary>
        /// API Lấy danh sách lịch sử/phiên đặt chỗ của tài xế đang đăng nhập.
        /// - CHỨC NĂNG: Tra cứu các phiên đặt chỗ đỗ xe cá nhân của tài xế.
        /// - ĐẦU VÀO: JWT Token (UserId).
        /// - ĐẦU RA: Gọi `_parkingQueryService.GetMyBookingsAsync` -> Trả về danh sách `MyBookingResponseDto`.
        /// </summary>
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

        /// <summary>
        /// API Hủy đặt chỗ trước khi xe vào check-in.
        /// - CHỨC NĂNG: Hủy phiên đặt giữ chỗ (Booking) và giải phóng ô đỗ về Available.
        /// - ĐẦU VÀO: Path Param `{sessionId}`, JWT Token (UserId).
        /// - ĐẦU RA: Gọi `_bookingService.CancelBookingAsync` -> Trả về kết quả hủy giữ chỗ.
        /// </summary>
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

        /// <summary>
        /// API Nhận diện biển số xe tự động qua AI và upload ảnh phương tiện.
        /// - CHỨC NĂNG: Đọc ảnh chụp xe, chạy mô hình AI nhận diện biển số song song với tải ảnh lên Cloudinary.
        /// - ĐẦU VÀO: Form Data `RecognizePlateRequest` (Chứa `ImageFile` và `VehicleTypeId`).
        /// - ĐẦU RA: Gọi `_aiRecognitionService` & `_imageStorageService` -> Trả về URL ảnh và chuỗi biển số nhận diện (`predictedPlate`).
        /// </summary>
        [Authorize(Roles = "Staff")]
        [HttpPost("recognize")]
        public async Task<IActionResult> RecognizePlate([FromForm] RecognizePlateRequest request)
        {
            try
            {
                if (request.ImageFile == null || request.ImageFile.Length == 0)
                    return BadRequest(new { isSuccess = false, message = "Vui lòng cung cấp file ảnh phương tiện." });

                // 1. Nếu là Xe đạp: Không cần chạy AI, tự tạo mã định danh ảo
                if (request.VehicleTypeId == 1) 
                {
                    var uploadResult = await _imageStorageService.UploadImageDetailedAsync(request.ImageFile, "parking_temp");
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

                // 2. Đọc file ảnh thành byte array một lần duy nhất để tránh xung đột stream khi chạy song song
                byte[] fileBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await request.ImageFile.CopyToAsync(memoryStream);
                    fileBytes = memoryStream.ToArray();
                }

                // 3. Tạo luồng nhớ độc lập cho tác vụ upload
                var uploadStream = new MemoryStream(fileBytes);

                var uploadFile = new FormFile(uploadStream, 0, fileBytes.Length, request.ImageFile.Name, request.ImageFile.FileName)
                {
                    Headers = request.ImageFile.Headers,
                    ContentType = request.ImageFile.ContentType
                };

                // 4. Chạy song song nhận diện AI và upload lên Cloudinary
                var aiTask = _aiRecognitionService.PredictLicensePlateFromBytesAsync(
                    fileBytes,
                    request.ImageFile.ContentType,
                    request.ImageFile.FileName);
                var uploadTask = _imageStorageService.UploadImageDetailedAsync(uploadFile, "parking_temp");

                try
                {
                    await Task.WhenAll(aiTask, uploadTask);
                }
                catch (Exception)
                {
                    // Bắt exception chung để tránh ném lỗi trực tiếp ra ngoài, sẽ xử lý cụ thể từng task
                }
                finally
                {
                    // Đảm bảo giải phóng các luồng nhớ sau khi hoàn tất
                    uploadStream.Dispose();
                }

                string detectedPlate = "";
                try
                {
                    detectedPlate = await aiTask;
                }
                catch (Exception aiEx)
                {
                    // Nếu AI lỗi, vẫn trả về URL ảnh đã upload thành công từ Cloudinary để nhân viên nhập tay
                    var completedUpload = await uploadTask;
                    return Ok(new
                    {
                        isSuccess = true,
                        imageUrl = completedUpload.OptimizedUrl,
                        rawImageUrl = completedUpload.RawUrl,
                        predictedPlate = "",
                        message = $"Không thể nhận dạng tự động: {aiEx.Message}. Vui lòng nhập tay."
                    });
                }

                var finalUploadResult = await uploadTask;

                return Ok(new
                {
                    isSuccess = true,
                    imageUrl = finalUploadResult.OptimizedUrl,
                    rawImageUrl = finalUploadResult.RawUrl,
                    predictedPlate = detectedPlate
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API Lấy danh sách tất cả các xe đang đỗ trong bãi (Active Sessions).
        /// - CHỨC NĂNG: Lấy thông tin chi tiết các phiên đỗ đang có trạng thái `InProgress`.
        /// - ĐẦU VÀO: Không cần tham số.
        /// - ĐẦU RA: Gọi `_parkingQueryService.GetActiveSessionsAsync` -> Trả về danh sách `ActiveSessionResponseDto`.
        /// </summary>
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

        /// <summary>
        /// API quét QR tại cổng vào (Check-in).
        /// - CHỨC NĂNG: Tiếp nhận mã QR hoặc mã vé thô, gọi Service giải mã và thực hiện Check-in.
        /// - ĐẦU VÀO: Path Param `{ticketCode}`, Query Param `detectedPlate`, JWT Token (StaffId).
        /// - ĐẦU RA: Gọi `_checkInService.ScanQrCheckInAsync` -> Trả về `ScanCheckInResponse`.
        /// </summary>
        [Authorize(Roles = "Staff")]
        [HttpGet("scan-checkin/{ticketCode}")]
        public async Task<IActionResult> ScanCheckIn(string ticketCode, [FromQuery] string? detectedPlate)
        {
            try
            {
                var staffIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(staffIdClaim))
                    return Unauthorized(new { isSuccess = false, message = "Không tìm thấy thông tin Staff thực hiện." });

                int currentStaffId = int.Parse(staffIdClaim);
                var response = await _checkInService.ScanQrCheckInAsync(ticketCode, detectedPlate, currentStaffId);
                if (!response.IsSuccess)
                    return BadRequest(response);

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { isSuccess = false, message = ex.Message });
            }
        }

        /// <summary>
        /// API quét QR tại cổng ra (Check-out).
        /// - CHỨC NĂNG: Tiếp nhận mã QR hoặc mã vé thô từ cổng ra, tính toán chi phí đỗ xe tạm tính.
        /// - ĐẦU VÀO: Path Param `{ticketCode}`, Query Param `detectedPlate`.
        /// - ĐẦU RA: Gọi `_checkOutService.ScanQrCheckOutAsync` -> Trả về `ScanCheckOutResponse`.
        /// </summary>
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

        /// <summary>
        /// API Định vị vị trí xe đang đỗ theo biển số.
        /// - CHỨC NĂNG: Tìm kiếm thông tin ô đỗ (Tầng, Tên ô) của một xe đang đỗ dựa trên biển số.
        /// - ĐẦU VÀO: Query Param `licensePlate`.
        /// - ĐẦU RA: Gọi `_parkingQueryService.LocateVehicleAsync` -> Trả về thông tin vị trí đỗ xe.
        /// </summary>
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
