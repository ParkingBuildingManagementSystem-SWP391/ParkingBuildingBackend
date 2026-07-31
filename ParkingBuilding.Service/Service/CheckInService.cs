using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using ParkingBuilding.Service.Service.Helpers;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    /// <summary>
    /// SERVICE LAYER: Xử lý nghiệp vụ Check-in xe vào bãi đỗ.
    /// - CHỨC NĂNG CHÍNH: Phân tích mã vé/QR, xác thực ca trực nhân viên, đối soát biển số, tự động gán vị trí đỗ và khởi tạo phiên đỗ xe mới (InProgress).
    /// - ĐẦU VÀO (Input): Nhận DTO `CheckInRequest` hoặc `WalkInRequest` truyền từ `ParkingController`.
    /// - ĐẦU RA (Output): Trả về `ScanCheckInResponse` hoặc `WalkInResponse` (kèm mã vé, tên vị trí đỗ, trạng thái) cho `ParkingController`.
    /// </summary>
    public class CheckInService : ICheckInService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ILogger<CheckInService> _logger;
        private readonly IImageStorageService _imageStorageService;
        private readonly IAiRecognitionService _aiRecognitionService;
        private readonly ParkingManagementDbContext _context;
        private readonly IStaffLogService _staffLogService;

        public CheckInService(
            IParkingRepository parkingRepository,
            ILogger<CheckInService> logger,
            IImageStorageService imageStorageService,
            IAiRecognitionService aiRecognitionService,
            ParkingManagementDbContext context,
            IStaffLogService staffLogService)
        {
            _parkingRepository = parkingRepository;
            _logger = logger;
            _imageStorageService = imageStorageService;
            _aiRecognitionService = aiRecognitionService;
            _context = context;
            _staffLogService = staffLogService;
        }

        // ================================================================
        // API #1: CHECK-IN CHÍNH (bằng mã vé/QR nhập tay hoặc quét được)
        // ================================================================
        public async Task<ScanCheckInResponse> CheckInVehicleAsync(CheckInRequest request, int currentStaffId)
        {
            await EnsureActiveShiftAsync(currentStaffId);

            if (request == null)
            {
                _logger.LogWarning("Check-in thất bại: Dữ liệu Request rỗng (null).");
                return new ScanCheckInResponse { IsSuccess = false, Message = "Dữ liệu Request rỗng (null)." };
            }

            // Nếu mã quét được là một chuỗi QR có cấu trúc "TICKET:...|PLATE:...", tách lấy đúng phần cần dùng
            if (QrCodeParserHelper.TryParseQr(request.TicketCode, out var parsedTicket, out var parsedPlate, out _, out _))
            {
                request.TicketCode = parsedTicket;
                if (string.IsNullOrEmpty(request.LicenseVehicle) || request.LicenseVehicle.Trim().ToLower() == "string")
                {
                    request.LicenseVehicle = parsedPlate;
                }
            }

            // Xử lý ảnh (nếu có): ưu tiên URL đã có sẵn, nếu không thì nhờ AI đọc biển số rồi upload lưu trữ
            string? checkInImageUrl = await ResolveCheckInImageAsync(request);

            _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé: {TicketCode}",
                request.LicenseVehicle, request.TicketCode);

            string? cleanTicketCode = NormalizeTicketCode(request.TicketCode);
            if (cleanTicketCode == null)
            {
                _logger.LogWarning("Check-in thất bại: Thiếu mã vé (Ticket Code) hoặc mã QR.");
                return new ScanCheckInResponse
                {
                    IsSuccess = false,
                    Message = "Vui lòng nhập mã vé (Ticket Code) hoặc quét mã QR để tiếp tục check-in."
                };
            }

            DateTime localNow = GetVietnamNow();
            MembershipCard? membershipCard = await FindActiveMembershipCardAsync(cleanTicketCode, localNow);
            ParkingSession? session = membershipCard == null
                ? await FindReservedSessionByTicketAsync(cleanTicketCode)
                : null;

            bool isBicycle = IsBicycle(membershipCard, session);
            // Xe đạp booking: dùng session.LicenseVehicle (BIKE_{GUID} lưu trong DB khi đặt chỗ) để check conflict.
            // Không dùng BIKE_BOOKING_{ticket} vì format đó không trùng với giá trị thật trong DB.
            string? cleanLicense = isBicycle
                ? (membershipCard != null
                    ? $"BIKE_MEMBERSHIP_{cleanTicketCode}"
                    : (session?.LicenseVehicle?.Trim().ToUpper() ?? $"BIKE_BOOKING_{cleanTicketCode}"))
                : ResolvePlate(request.LicenseVehicle, throwOnInvalidFormat: true);

            var conflict = await GetActiveParkingConflictAsync(cleanLicense);
            if (conflict != null)
            {
                return conflict;
            }

            if (membershipCard != null)
            {
                return await ProcessMembershipCardCheckInAsync(membershipCard, cleanLicense, checkInImageUrl, currentStaffId, sourceTag: "");
            }

            return await ProcessBookingCheckInAsync(session, cleanLicense, cleanTicketCode, checkInImageUrl, currentStaffId);
        }

        // ================================================================
        // API #2: CHECK-IN VÃNG LAI (không có mã vé/đặt chỗ trước)
        // ================================================================
        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request, int currentStaffId)
        {
            await EnsureActiveShiftAsync(currentStaffId);

            if (request == null)
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Yêu cầu dữ liệu không hợp lệ!" };
            }

            string? checkInImageUrl = null;
            if (!string.IsNullOrEmpty(request.ImageUrl))
            {
                checkInImageUrl = request.ImageUrl;
            }

            if (request.VehicleTypeId == 1) // Xe đạp
            {
                if (string.IsNullOrEmpty(request.LicenseVehicle) || request.LicenseVehicle == "string" || !request.LicenseVehicle.StartsWith("BIKE_"))
                {
                    request.LicenseVehicle = $"BIKE_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}";
                }
            }
            else if (string.IsNullOrEmpty(request.LicenseVehicle) || request.LicenseVehicle == "string")
            {
                if (request.ImageFile != null && request.ImageFile.Length > 0)
                {
                    try
                    {
                        // 1. Nhận diện từ file trực tiếp
                        string detectedPlate = await _aiRecognitionService.PredictLicensePlateFromFileAsync(request.ImageFile);
                        request.LicenseVehicle = detectedPlate;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("AI Nhận diện Walk-in lỗi: {Msg}", ex.Message);
                    }
                }
            }

            // 2. Upload Cloudinary sau khi AI nhận dạng
            if (string.IsNullOrEmpty(checkInImageUrl) && request.ImageFile != null && request.ImageFile.Length > 0)
            {
                checkInImageUrl = await _imageStorageService.UploadImageAsync(request.ImageFile, "parking_checkin");
            }

            if (string.IsNullOrWhiteSpace(request.LicenseVehicle) || request.LicenseVehicle.Trim().ToLower() == "string")
            {
                return new WalkInResponse { Status = "Error", TicketCode = "Vui lòng cung cấp biển số xe!" };
            }

            string cleanLicense;
            if (request.VehicleTypeId == 1 || request.LicenseVehicle.StartsWith("BIKE_"))
            {
                cleanLicense = request.LicenseVehicle.Trim().ToUpper();
            }
            else
            {
                if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string cleanedLicense))
                {
                    return new WalkInResponse
                    {
                        Status = "Error",
                        TicketCode = LicensePlateHelper.GetErrorMessage()
                    };
                }
                cleanLicense = cleanedLicense;
            }

            var activeSession = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
            if (activeSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} hiện đang có một lượt đỗ chưa hoàn thành trong bãi!"
                };
            }

            var reservedSession = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
            if (reservedSession != null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"HỆ THỐNG CHẶN: Phương tiện {cleanLicense} đang có lịch ĐẶT TRƯỚC chưa check-in!"
                };
            }

            // Tạo vé vãng lai mới và lưu session
            var ticket = new Ticket
            {
                TicketCode = $"WK_{Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}",
                TicketStatus = ParkingStatuses.TicketActive
            };

            var newSession = await _parkingRepository.CreateWalkInSessionWithLockAsync(cleanLicense, request.VehicleTypeId, checkInImageUrl, ticket);
            if (newSession == null)
            {
                return new WalkInResponse { Status = "Full", TicketCode = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!" };
            }

            var slot = newSession.Slot ?? await _parkingRepository.GetSlotByIdAsync(newSession.SlotId);
            if (slot == null)
            {
                return new WalkInResponse
                {
                    Status = "Error",
                    TicketCode = $"Hệ thống không tìm thấy slot id tương ứng"
                };
            }

            await _staffLogService.LogActivityAsync(
                currentStaffId,
                "CHECK_IN",
                $"Check-in vãng lai (walk-in) thành công cho xe {newSession.LicenseVehicle} tại ô {slot.SlotName}. Vé vãng lai: {ticket.TicketCode}.",
                newSession.LicenseVehicle,
                newSession.SessionId
            );

            return new WalkInResponse
            {
                SessionId = newSession.SessionId,
                SlotId = slot.SlotId,
                TicketCode = ticket.TicketCode,
                SlotName = slot.SlotName,
                LicenseVehicle = newSession.LicenseVehicle,
                CheckInTime = newSession.CheckInTime ?? DateTime.UtcNow,
                Status = ParkingStatuses.SessionInProgress
            };
        }

        // ================================================================
        // API #3: QUÉT QR XÁC THỰC (bước xem trước trước khi check-in chính)
        // ================================================================
        public async Task<ScanCheckInResponse> ScanQrCheckInAsync(string ticketCode, string? detectedPlate, int currentStaffId)
        {
            await EnsureActiveShiftAsync(currentStaffId);

            if (QrCodeParserHelper.TryParseQr(ticketCode, out var parsedTicket, out var parsedPlate, out _, out _))
            {
                ticketCode = parsedTicket!;
                if (string.IsNullOrWhiteSpace(detectedPlate) || detectedPlate.Trim().ToLower() == "string")
                {
                    detectedPlate = parsedPlate;
                }
            }

            string? cleanTicketCode = NormalizeTicketCode(ticketCode);
            if (cleanTicketCode == null)
            {
                _logger.LogWarning("Scan check-in thất bại: Thiếu mã vé hoặc mã QR.");
                return new ScanCheckInResponse { IsSuccess = false, Message = "Vui lòng nhập mã vé (Ticket Code) hoặc quét mã QR để tiếp tục check-in." };
            }

            DateTime localNow = GetVietnamNow();
            MembershipCard? membershipCard = await FindActiveMembershipCardAsync(cleanTicketCode, localNow);
            ParkingSession? session = membershipCard == null
                ? await FindReservedSessionByTicketAsync(cleanTicketCode)
                : null;

            bool isBicycle = IsBicycle(membershipCard, session);
            // Xe đạp booking: dùng session.LicenseVehicle (BIKE_{GUID} lưu trong DB) để check conflict.
            string? cleanLicense = isBicycle
                ? (membershipCard != null
                    ? $"BIKE_MEMBERSHIP_{cleanTicketCode}"
                    : (session?.LicenseVehicle?.Trim().ToUpper() ?? $"BIKE_BOOKING_{cleanTicketCode}"))
                : ResolvePlate(detectedPlate, throwOnInvalidFormat: false);

            var conflict = await GetActiveParkingConflictAsync(cleanLicense);
            if (conflict != null)
            {
                return conflict;
            }

            if (membershipCard != null)
            {
                // Nhánh thẻ thành viên: giống hệt check-in chính, THẬT SỰ tạo phiên đỗ và ghi vào CSDL.
                // (Không truyền ảnh vì endpoint quét QR không nhận tham số ảnh.)
                return await ProcessMembershipCardCheckInAsync(membershipCard, cleanLicense, checkInImageUrl: null, currentStaffId, sourceTag: " (ScanQr)");
            }

            // Nhánh đặt chỗ: CHỈ xác thực và trả về thông tin xem trước, KHÔNG ghi gì vào CSDL.
            // Muốn chốt thật vẫn phải gọi CheckInVehicleAsync (API check-in chính).
            return BuildBookingScanPreview(session, detectedPlate, cleanTicketCode);
        }

        // ================================================================
        // HELPER — Dùng chung cho cả ca trực, chuẩn hoá dữ liệu đầu vào
        // ================================================================

        private async Task EnsureActiveShiftAsync(int staffId)
        {
            var activeShift = await _staffLogService.GetActiveShiftAsync(staffId);
            if (activeShift == null)
            {
                throw new InvalidOperationException("Bạn chưa mở ca làm việc. Vui lòng mở ca trực trước khi thao tác.");
            }
        }

        /// <summary>
        /// Làm sạch mã vé: coi các chuỗi rỗng, chỉ có khoảng trắng, hoặc các chuỗi "null"/"undefined"
        /// (dấu vết lỗi thường gặp từ phía Frontend) đều là KHÔNG có mã vé.
        /// </summary>
        private static string? NormalizeTicketCode(string? rawTicketCode)
        {
            if (string.IsNullOrWhiteSpace(rawTicketCode))
            {
                return null;
            }

            string trimmed = rawTicketCode.Trim();
            string lower = trimmed.ToLower();
            if (lower == "null" || lower == "undefined")
            {
                return null;
            }

            return trimmed;
        }

        private static DateTime GetVietnamNow()
        {
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);
        }

        private static string GetVehicleTypeName(int typeId) => typeId switch
        {
            1 => "Xe đạp",
            2 => "Xe máy",
            _ => "Xe hơi"
        };

        /// <summary>
        /// Xử lý ảnh chụp cổng vào cho luồng check-in chính: nếu đã có URL ảnh sẵn thì dùng luôn;
        /// nếu có file ảnh thật thì nhờ AI đọc biển số (ghi đè vào request.LicenseVehicle nếu đọc được)
        /// rồi tải ảnh lên kho lưu trữ.
        /// </summary>
        private async Task<string?> ResolveCheckInImageAsync(CheckInRequest request)
        {
            if (!string.IsNullOrEmpty(request.ImageUrl))
            {
                return request.ImageUrl;
            }

            if (request.ImageFile == null || request.ImageFile.Length == 0)
            {
                return null;
            }

            try
            {
                string detectedPlate = await _aiRecognitionService.PredictLicensePlateFromFileAsync(request.ImageFile);
                request.LicenseVehicle = detectedPlate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("AI Nhận diện cổng vào lỗi: {Msg}. Tiếp tục dùng biển số nhập tay nếu có.", ex.Message);
            }

            return await _imageStorageService.UploadImageAsync(request.ImageFile, "parking_checkin");
        }

        // ================================================================
        // HELPER — Tra cứu Thẻ Thành Viên / Đặt Chỗ theo mã vé
        // ================================================================

        private async Task<MembershipCard?> FindActiveMembershipCardAsync(string cleanTicketCode, DateTime localNow)
        {
            return await _context.MembershipCards
                .Include(mc => mc.Ticket)
                .Include(mc => mc.Tier)
                .Include(mc => mc.User)
                .Include(mc => mc.MembershipSlots)
                    .ThenInclude(ms => ms.Slot)
                .Include(mc => mc.MembershipVehicles)
                .FirstOrDefaultAsync(mc => mc.Ticket.TicketCode.Trim() == cleanTicketCode
                                     && mc.Status == ParkingStatuses.MonthlyCardActive
                                     && !mc.IsDeleted
                                     && mc.EndTime >= localNow);
        }

        /// <summary>
        /// Tra một đặt chỗ (booking) đang "Reserved" theo mã vé đã nhập. Nếu mã vé chuyển đổi được
        /// thành số, thử tra nhanh theo TicketId nội bộ — NHƯNG luôn xác minh lại mã vé THẬT của
        /// session tìm được có khớp với chuỗi đã nhập hay không, để không ai có thể "dò" số ID nội
        /// bộ và check-in nhầm vào đặt chỗ của người khác.
        /// </summary>
        private async Task<ParkingSession?> FindReservedSessionByTicketAsync(string cleanTicketCode)
        {
            ParkingSession? session;

            if (int.TryParse(cleanTicketCode, out int ticketId))
            {
                session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);

                bool ticketCodeMatches = session != null &&
                    string.Equals(session.Ticket?.TicketCode?.Trim(), cleanTicketCode, StringComparison.OrdinalIgnoreCase);

                if (!ticketCodeMatches)
                {
                    session = null;
                }
            }
            else
            {
                session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
            }

            return session;
        }

        private static bool IsBicycle(MembershipCard? membershipCard, ParkingSession? session)
        {
            return (membershipCard != null && membershipCard.Tier.TypeId == 1)
                || (session != null && session.TypeId == 1);
        }

        /// <summary>
        /// Chuẩn hoá và xác thực biển số (KHÔNG áp dụng cho xe đạp — xe đạp dùng mã định danh riêng,
        /// xử lý ở nơi gọi). Trả về null nếu không có biển số hoặc biển số rỗng/placeholder.
        /// </summary>
        private static string? ResolvePlate(string? rawPlate, bool throwOnInvalidFormat)
        {
            if (string.IsNullOrWhiteSpace(rawPlate) || rawPlate.Trim().ToLower() == "string")
            {
                return null;
            }

            if (rawPlate.StartsWith("BIKE_"))
            {
                return rawPlate.Trim().ToUpper();
            }

            if (LicensePlateHelper.IsValidLicensePlate(rawPlate, out string validatedPlate))
            {
                return validatedPlate;
            }

            if (throwOnInvalidFormat)
            {
                throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
            }

            return null;
        }

        /// <summary>
        /// Chặn xe đã có một phiên đỗ đang hoạt động (SessionStatus = InProgress) khớp biển số —
        /// dùng chung cho cả check-in chính lẫn quét QR xác thực.
        /// </summary>
        private async Task<ScanCheckInResponse?> GetActiveParkingConflictAsync(string? cleanLicense)
        {
            if (cleanLicense == null)
            {
                return null;
            }

            var alreadyInParking = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
            if (alreadyInParking == null)
            {
                return null;
            }

            _logger.LogWarning("Check-in thất bại: Xe biển số '{LicensePlate}' đã có một phiên đỗ xe đang hoạt động trong bãi (SessionId: {SessionId}).",
                cleanLicense, alreadyInParking.SessionId);

            return new ScanCheckInResponse
            {
                IsSuccess = false,
                Message = $"Xe biển số '{cleanLicense}' đã có một phiên đỗ xe đang hoạt động trong bãi."
            };
        }

        // ================================================================
        // HELPER — Nhánh Thẻ Thành Viên (dùng chung cho Check-In chính & Quét QR)
        // ================================================================

        private async Task<ScanCheckInResponse> ProcessMembershipCardCheckInAsync(
            MembershipCard membershipCard,
            string? cleanLicense,
            string? checkInImageUrl,
            int currentStaffId,
            string sourceTag)
        {
            // 1. Với xe không phải xe đạp, biển số phải nằm trong danh sách đã đăng ký và đang hoạt động của thẻ
            if (membershipCard.Tier.TypeId != 1 && !(cleanLicense != null && cleanLicense.StartsWith("BIKE_")))
            {
                if (string.IsNullOrWhiteSpace(cleanLicense))
                {
                    return new ScanCheckInResponse
                    {
                        IsSuccess = false,
                        Message = "Thẻ thành viên này yêu cầu phải có biển số xe khi check-in."
                    };
                }

                bool isRegisteredPlate = membershipCard.MembershipVehicles
                    .Any(v => v.LicenseVehicle.Trim().ToUpper() == cleanLicense.Trim().ToUpper() && v.IsActive);

                if (!isRegisteredPlate)
                {
                    return new ScanCheckInResponse
                    {
                        IsSuccess = false,
                        Message = $"Xe biển số '{cleanLicense}' không được đăng ký hoặc không hoạt động trong gói thẻ thành viên này.",
                        SessionId = 0,
                        LicenseVehicle = cleanLicense,
                        SlotName = "N/A",
                        VehicleTypeName = GetVehicleTypeName(membershipCard.Tier.TypeId),
                        DriverName = membershipCard.User?.Username ?? "N/A",
                        DriverPhone = membershipCard.User?.PhoneNumber ?? "N/A",
                        DriverEmail = membershipCard.User?.Email ?? "N/A",
                        TicketCode = membershipCard.Ticket.TicketCode
                    };
                }
                // Không cần kiểm tra lại "biển số đã đỗ trong bãi chưa" ở đây nữa — GetActiveParkingConflictAsync
                // đã kiểm tra điều này TRƯỚC KHI vào nhánh thẻ thành viên rồi (tránh kiểm tra trùng lặp 2 lần).
            }

            // 2. Nếu chủ thẻ đã có một xe khác cùng loại đang đỗ trong bãi -> tự động chuyển xe này sang chế độ vãng lai có tính phí
            var activeMemberSession = await _context.ParkingSessions
                .FirstOrDefaultAsync(s => s.UserId == membershipCard.UserId
                                      && s.TypeId == membershipCard.Tier.TypeId
                                      && s.SessionStatus == ParkingStatuses.SessionInProgress
                                      && !s.IsDeleted);

            if (activeMemberSession != null)
            {
                return await ConvertMembershipSecondVehicleToWalkInAsync(membershipCard, cleanLicense, checkInImageUrl, currentStaffId, sourceTag);
            }

            // 3. Dùng ô đỗ cố định của thẻ
            return await AssignMembershipFixedSlotAsync(membershipCard, cleanLicense, checkInImageUrl, currentStaffId, sourceTag);
        }

        private async Task<ScanCheckInResponse> ConvertMembershipSecondVehicleToWalkInAsync(
            MembershipCard membershipCard,
            string? cleanLicense,
            string? checkInImageUrl,
            int currentStaffId,
            string sourceTag)
        {
            var slotForWalkIn = await _context.ParkingSlots
                .FromSqlInterpolated($"SELECT TOP 1 * FROM ParkingSlots WITH (UPDLOCK, ROWLOCK) WHERE SlotStatus = {ParkingStatuses.SlotAvailable} AND TypeId = {membershipCard.Tier.TypeId} AND IsDeleted = 0 ORDER BY SlotName ASC")
                .FirstOrDefaultAsync();

            if (slotForWalkIn == null)
            {
                return new ScanCheckInResponse
                {
                    IsSuccess = false,
                    Message = "Thành thật xin lỗi, bãi xe hiện tại đã hết chỗ trống cho loại xe này!"
                };
            }

            slotForWalkIn.SlotStatus = ParkingStatuses.SlotOccupied;
            _context.ParkingSlots.Update(slotForWalkIn);

            var newWalkInSession = new ParkingSession
            {
                UserId = null, // Set to null for paid session
                SlotId = slotForWalkIn.SlotId,
                LicenseVehicle = cleanLicense ?? $"MBC_VEHICLE_{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}",
                TypeId = membershipCard.Tier.TypeId,
                CheckInTime = DateTime.UtcNow,
                CheckInImageUrl = checkInImageUrl,
                SessionStatus = ParkingStatuses.SessionInProgress,
                TicketId = membershipCard.TicketId,
                IsDeleted = false
            };

            await _context.ParkingSessions.AddAsync(newWalkInSession);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Tự động chuyển check-in vãng lai{Source}: Xe '{Plate}' vào bãi. Ô đỗ: {SlotName}.",
                sourceTag, newWalkInSession.LicenseVehicle, slotForWalkIn.SlotName);

            await _staffLogService.LogActivityAsync(
                currentStaffId,
                "CHECK_IN",
                $"Thẻ tháng đã có xe trong bãi{sourceTag}. Tự động chuyển sang check-in vãng lai cho xe {newWalkInSession.LicenseVehicle} tại ô {slotForWalkIn.SlotName}.",
                newWalkInSession.LicenseVehicle,
                newWalkInSession.SessionId
            );

            return new ScanCheckInResponse
            {
                IsSuccess = true,
                Message = $"Thẻ thành viên đã có xe trong bãi. Tự động chuyển sang check-in vãng lai. Vui lòng đỗ vào ô: {slotForWalkIn.SlotName}.",
                SessionId = newWalkInSession.SessionId,
                LicenseVehicle = newWalkInSession.LicenseVehicle,
                SlotName = slotForWalkIn.SlotName,
                VehicleTypeName = GetVehicleTypeName(membershipCard.Tier.TypeId),
                TicketCode = membershipCard.Ticket.TicketCode,
                RequiresPayment = true,
                PaymentStatus = "PENDING",
                DriverName = membershipCard.User?.Username,
                DriverPhone = membershipCard.User?.PhoneNumber,
                DriverEmail = membershipCard.User?.Email
            };
        }

        private async Task<ScanCheckInResponse> AssignMembershipFixedSlotAsync(
            MembershipCard membershipCard,
            string? cleanLicense,
            string? checkInImageUrl,
            int currentStaffId,
            string sourceTag)
        {
            var availableMs = membershipCard.MembershipSlots
                .FirstOrDefault(ms => ms.Slot != null
                                   && (ms.Slot.SlotStatus.Trim() == ParkingStatuses.SlotReserved
                                       || ms.Slot.SlotStatus.Trim() == ParkingStatuses.SlotAvailable)
                                   && !ms.Slot.IsDeleted);

            if (availableMs == null)
            {
                var slotNames = string.Join(", ", membershipCard.MembershipSlots
                    .Select(ms => ms.Slot?.SlotName ?? "?"));
                return new ScanCheckInResponse
                {
                    IsSuccess = false,
                    Message = $"Không tìm thấy ô đỗ rảnh. Các ô đỗ ({slotNames}) hiện đang bị chiếm."
                };
            }

            var slot = availableMs.Slot!;

            var sessionTicket = new Ticket
            {
                TicketCode = membershipCard.Ticket.TicketCode,
                TicketStatus = ParkingStatuses.TicketActive
            };
            await _context.Tickets.AddAsync(sessionTicket);
            await _context.SaveChangesAsync();

            var newSession = new ParkingSession
            {
                UserId = membershipCard.UserId,
                SlotId = slot.SlotId,
                LicenseVehicle = cleanLicense ?? $"MBC_VEHICLE_{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}",
                TypeId = membershipCard.Tier.TypeId,
                CheckInTime = DateTime.UtcNow,
                CheckInImageUrl = checkInImageUrl,
                SessionStatus = ParkingStatuses.SessionInProgress,
                TicketId = sessionTicket.TicketId,
                IsDeleted = false
            };

            slot.SlotStatus = ParkingStatuses.SlotOccupied;
            _context.ParkingSlots.Update(slot);

            await _context.ParkingSessions.AddAsync(newSession);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Check-in THẺ THÀNH VIÊN thành công{Source}: Xe '{Plate}' vào bãi. Ô đỗ cố định: {SlotName}.",
                sourceTag, newSession.LicenseVehicle, slot.SlotName);

            await _staffLogService.LogActivityAsync(
                currentStaffId,
                "CHECK_IN",
                $"Check-in thẻ thành viên thành công{sourceTag} cho xe {newSession.LicenseVehicle} tại ô đỗ cố định {slot.SlotName}.",
                newSession.LicenseVehicle,
                newSession.SessionId
            );

            return new ScanCheckInResponse
            {
                IsSuccess = true,
                Message = $"Check-in thẻ thành viên thành công! Vui lòng đỗ xe vào vị trí đỗ cố định của bạn: {slot.SlotName}.",
                SessionId = newSession.SessionId,
                LicenseVehicle = newSession.LicenseVehicle,
                SlotName = slot.SlotName,
                VehicleTypeName = GetVehicleTypeName(membershipCard.Tier.TypeId),
                TicketCode = membershipCard.Ticket.TicketCode,
                RequiresPayment = false,
                PaymentStatus = "SUCCESS",
                DriverName = membershipCard.User?.Username,
                DriverPhone = membershipCard.User?.PhoneNumber,
                DriverEmail = membershipCard.User?.Email
            };
        }

        // ================================================================
        // HELPER — Nhánh Đặt Chỗ (Booking)
        // ================================================================

        /// <summary>
        /// Dùng bởi CheckInVehicleAsync: XÁC NHẬN THẬT, chuyển session từ "Reserved" sang "InProgress"
        /// và ghi vào CSDL.
        /// </summary>
        private async Task<ScanCheckInResponse> ProcessBookingCheckInAsync(
            ParkingSession? session,
            string? cleanLicense,
            string? cleanTicketCode,
            string? checkInImageUrl,
            int currentStaffId)
        {
            if (session != null && session.TypeId != 1 && cleanLicense != null && !string.IsNullOrEmpty(session.LicenseVehicle))
            {
                if (cleanLicense.ToUpper() != session.LicenseVehicle.Trim().ToUpper())
                {
                    return new ScanCheckInResponse
                    {
                        IsSuccess = false,
                        Message = $"Biển số thực tế '{cleanLicense}' không khớp với biển số đã đăng ký đặt chỗ '{session.LicenseVehicle}'."
                    };
                }
            }

            if (session == null)
            {
                _logger.LogWarning("Check-in thất bại: Không tìm thấy phiên đặt chỗ (Reservation) tương ứng với Biển số '{License}' hoặc Mã vé '{Ticket}'.",
                    cleanLicense, cleanTicketCode);
                return new ScanCheckInResponse
                {
                    IsSuccess = false,
                    Message = $"Không tìm thấy phiên đặt chỗ tương ứng với Biển số '{cleanLicense}' hoặc Mã vé '{cleanTicketCode}'."
                };
            }

            session.SessionStatus = ParkingStatuses.SessionInProgress;
            session.CheckInTime = DateTime.UtcNow;
            session.CheckInImageUrl = checkInImageUrl;

            if (session.Slot != null)
            {
                if (session.Slot.SlotStatus.Trim() == ParkingStatuses.SlotOccupied)
                {
                    _logger.LogError("Check-in thất bại: Vị trí đỗ {SlotName} đã bị xe khác chiếm dụng cho Session {SessionId}.",
                        session.Slot.SlotName, session.SessionId);
                    throw new Exception($"Chỗ đỗ {session.Slot.SlotName} hiện đã bị xe khác chiếm dụng. Vui lòng kiểm tra lại vị trí đỗ.");
                }
                session.Slot.SlotStatus = ParkingStatuses.SlotOccupied;
            }

            if (cleanTicketCode != null && session.Ticket != null)
            {
                session.Ticket.TicketStatus = ParkingStatuses.TicketActive;
            }

            await _parkingRepository.UpdateSessionAndSlotAsync(session, session.Slot!);

            _logger.LogInformation("Check-in THÀNH CÔNG: Xe '{LicensePlate}' đã vào bãi. Ô đỗ phân phối: {SlotName}. SessionId: {SessionId}.",
                cleanLicense ?? session.LicenseVehicle, session.Slot?.SlotName ?? "N/A", session.SessionId);

            await _staffLogService.LogActivityAsync(
                currentStaffId,
                "CHECK_IN",
                $"Check-in đặt trước thành công cho xe {session.LicenseVehicle} tại vị trí {session.Slot?.SlotName ?? "N/A"}.",
                session.LicenseVehicle,
                session.SessionId
            );

            return new ScanCheckInResponse
            {
                IsSuccess = true,
                Message = "Check-in thành công! Mời xe tiến qua thanh chắn vào bãi.",
                SessionId = session.SessionId,
                LicenseVehicle = session.LicenseVehicle,
                SlotName = session.Slot?.SlotName ?? "N/A",
                VehicleTypeName = session.Type?.TypeName ?? "N/A",
                ExpectedCheckInTime = session.ExpectedCheckInTime,
                BookingTime = session.BookingTime,
                DriverName = session.User?.Username ?? "N/A",
                DriverPhone = session.User?.PhoneNumber ?? "N/A",
                DriverEmail = session.User?.Email ?? "N/A",
                TicketCode = session.Ticket?.TicketCode ?? cleanTicketCode ?? "",
                RequiresPayment = session.Invoice != null && session.Invoice.PaymentStatus == "PENDING",
                PaymentStatus = session.Invoice?.PaymentStatus ?? "NoPayment"
            };
        }

        /// <summary>
        /// Dùng bởi ScanQrCheckInAsync: CHỈ xác thực và trả về thông tin xem trước, KHÔNG ghi gì vào CSDL.
        /// </summary>
        private static ScanCheckInResponse BuildBookingScanPreview(ParkingSession? session, string? detectedPlate, string cleanTicketCode)
        {
            if (session == null)
            {
                return new ScanCheckInResponse { IsSuccess = false, Message = "Không tìm thấy thông tin đặt chỗ." };
            }

            if (session.TypeId != 1 && !string.IsNullOrEmpty(detectedPlate) && detectedPlate.Trim().ToLower() != "string")
            {
                var cleanDetected = detectedPlate.Trim().Replace("-", "").Replace(".", "").ToUpper();
                var cleanRegistered = session.LicenseVehicle.Trim().Replace("-", "").Replace(".", "").ToUpper();

                if (cleanRegistered != cleanDetected)
                {
                    return new ScanCheckInResponse
                    {
                        IsSuccess = false,
                        Message = $"Cảnh báo bảo mật: Vé QR đăng ký cho xe {session.LicenseVehicle}, không khớp với xe thực tế tại cổng là {detectedPlate}!"
                    };
                }
            }

            return new ScanCheckInResponse
            {
                IsSuccess = true,
                Message = "Quét mã QR và đối khớp biển số thành công.",
                SessionId = session.SessionId,
                LicenseVehicle = session.LicenseVehicle,
                SlotName = session.Slot?.SlotName ?? "N/A",
                VehicleTypeName = session.Type?.TypeName ?? "N/A",
                ExpectedCheckInTime = session.ExpectedCheckInTime,
                BookingTime = session.BookingTime,
                DriverName = session.User?.Username ?? "N/A",
                DriverPhone = session.User?.PhoneNumber ?? "N/A",
                DriverEmail = session.User?.Email ?? "N/A",
                TicketCode = session.Ticket?.TicketCode ?? cleanTicketCode,
                RequiresPayment = session.Invoice != null && session.Invoice.PaymentStatus == "PENDING",
                PaymentStatus = session.Invoice?.PaymentStatus ?? "NoPayment"
            };
        }
    }
}
