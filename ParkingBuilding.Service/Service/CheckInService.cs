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
    public class CheckInService : ICheckInService
    {
        private readonly IParkingRepository _parkingRepository;
        private readonly ILogger<CheckInService> _logger;
        private readonly IImageStorageService _imageStorageService;
        private readonly IAiRecognitionService _aiRecognitionService;
        private readonly ParkingManagementDbContext _context;

        public CheckInService(
            IParkingRepository parkingRepository,
            ILogger<CheckInService> logger,
            IImageStorageService imageStorageService,
            IAiRecognitionService aiRecognitionService,
            ParkingManagementDbContext context)
        {
            _parkingRepository = parkingRepository;
            _logger = logger;
            _imageStorageService = imageStorageService;
            _aiRecognitionService = aiRecognitionService;
            _context = context;
        }

        public async Task<ScanCheckInResponse> CheckInVehicleAsync(CheckInRequest request)
        {
            if (request == null)
            {
                _logger.LogWarning("Check-in thất bại: Dữ liệu Request rỗng (null).");
                return new ScanCheckInResponse { IsSuccess = false, Message = "Dữ liệu Request rỗng (null)." };
            }

            if (QrCodeParserHelper.TryParseQr(request.TicketCode, out var parsedTicket, out var parsedPlate, out var parsedSessionId, out var parsedSlot))
            {
                request.TicketCode = parsedTicket;
                if (string.IsNullOrEmpty(request.LicenseVehicle) || request.LicenseVehicle.Trim().ToLower() == "string")
                {
                    request.LicenseVehicle = parsedPlate;
                }
            }

            _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé/Mã QR: {TicketCode}",
                request.LicenseVehicle, request.TicketCode);

            string? checkInImageUrl = null;
            if (!string.IsNullOrEmpty(request.ImageUrl))
            {
                // Lấy trực tiếp URL ảnh đã qua xác nhận ở Frontend
                checkInImageUrl = request.ImageUrl;
            }
            else if (request.ImageFile != null && request.ImageFile.Length > 0)
            {
                // Trường hợp chạy tự động, không có xác nhận thủ công từ FE
                try
                {
                    // 1. Gửi trực tiếp file ảnh sang AI
                    string detectedPlate = await _aiRecognitionService.PredictLicensePlateFromFileAsync(request.ImageFile);
                    request.LicenseVehicle = detectedPlate;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("AI Nhận diện cổng vào lỗi: {Msg}. Tiếp tục dùng biển số nhập tay nếu có.", ex.Message);
                }

                // 2. Sau đó mới upload Cloudinary
                checkInImageUrl = await _imageStorageService.UploadImageAsync(request.ImageFile, "parking_checkin");
            }
                _logger.LogInformation("Bắt đầu xử lý check-in cho xe biển số: {LicensePlate}, Vé: {TicketCode}",
                request.LicenseVehicle, request.TicketCode);
            string? cleanTicketCode = string.IsNullOrWhiteSpace(request.TicketCode) ? null : request.TicketCode.Trim();
            string? cleanLicense = null;
            if (!string.IsNullOrWhiteSpace(request.LicenseVehicle) && request.LicenseVehicle.Trim().ToLower() != "string")
            {
                if (request.LicenseVehicle.StartsWith("BIKE_"))
                {
                    cleanLicense = request.LicenseVehicle.Trim().ToUpper();
                }
                else
                {
                    if (!LicensePlateHelper.IsValidLicensePlate(request.LicenseVehicle, out string validatedPlate))
                    {
                        throw new ArgumentException(LicensePlateHelper.GetErrorMessage());
                    }
                    cleanLicense = validatedPlate;
                }
            }

            if (cleanTicketCode == null && cleanLicense == null)
            {
                _logger.LogWarning("Check-in thất bại: Cả Biển số xe và Mã vé đều rỗng.");
                return new ScanCheckInResponse { IsSuccess = false, Message = "Cả Biển số xe và Mã vé đều rỗng." };
            }

            if (cleanLicense != null)
            {
                var alreadyInParking = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
                if (alreadyInParking != null)
                {
                    _logger.LogWarning("Check-in thất bại: Xe biển số '{LicensePlate}' đã có một phiên đỗ xe đang hoạt động trong bãi (SessionId: {SessionId}).",
                        cleanLicense, alreadyInParking.SessionId);
                    return new ScanCheckInResponse 
                    { 
                        IsSuccess = false, 
                        Message = $"Xe biển số '{cleanLicense}' đã có một phiên đỗ xe đang hoạt động trong bãi." 
                    };
                }
            }

            ParkingSession? session = null;

            if (cleanTicketCode != null)
            {
                // Kiểm tra xem TicketCode quét vào có thuộc một thẻ thành viên ACTIVE và còn hạn hay không
                var membershipCard = await _context.MembershipCards
                    .Include(mc => mc.Ticket)
                    .Include(mc => mc.Tier)
                    .Include(mc => mc.User)
                    .Include(mc => mc.Slot)
                    .Include(mc => mc.MembershipVehicles)
                    .FirstOrDefaultAsync(mc => mc.Ticket.TicketCode.Trim() == cleanTicketCode.Trim()
                                         && mc.Status == ParkingStatuses.MonthlyCardActive
                                         && !mc.IsDeleted
                                         && mc.EndTime >= DateTime.UtcNow);

                if (membershipCard != null)
                {
                    // >>> KỊCH BẢN CHECK-IN BẰNG THẺ THÀNH VIÊN <<<

                    // 1. Kiểm tra xem thẻ thành viên này có đang được xe khác dùng trong bãi hay không
                    var activeSessionWithTicket = await _context.ParkingSessions
                        .FirstOrDefaultAsync(s => s.TicketId == membershipCard.TicketId
                                             && s.SessionStatus == ParkingStatuses.SessionInProgress
                                             && !s.IsDeleted);

                    if (activeSessionWithTicket != null)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            RequiresWalkIn = true,
                            Message = $"Vé thành viên này hiện đang được sử dụng cho xe biển số {activeSessionWithTicket.LicenseVehicle} trong bãi. Vui lòng chuyển sang check-in vãng lai."
                        };
                    }

                    // 2. Kiểm tra biển số xe đi vào có thuộc danh sách biển số đã đăng ký và đang hoạt động không
                    if (cleanLicense != null)
                    {
                        var isRegisteredPlate = membershipCard.MembershipVehicles
                            .Any(v => v.LicenseVehicle.Trim().ToUpper() == cleanLicense.Trim().ToUpper() && v.IsActive);

                        if (!isRegisteredPlate)
                        {
                            return new ScanCheckInResponse
                            {
                                IsSuccess = false,
                                Message = $"Xe biển số '{cleanLicense}' không được đăng ký hoặc không hoạt động trong gói thẻ thành viên này."
                            };
                        }

                        // 3. Kiểm tra xem biển số xe đã có một lượt đỗ hoạt động chưa
                        var alreadyInParking = await _parkingRepository.GetActiveSessionByLicensePlateAsync(cleanLicense);
                        if (alreadyInParking != null)
                        {
                            return new ScanCheckInResponse
                            {
                                IsSuccess = false,
                                Message = $"Xe biển số '{cleanLicense}' đã có một phiên đỗ xe đang hoạt động trong bãi."
                            };
                        }
                    }

                    // 4. Lấy ô đỗ cố định đã đăng ký của thẻ thành viên
                    if (!membershipCard.SlotId.HasValue)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            Message = "Thẻ thành viên chưa được liên kết với ô đỗ xe cố định nào."
                        };
                    }

                    var slot = await _context.ParkingSlots
                        .FirstOrDefaultAsync(s => s.SlotId == membershipCard.SlotId.Value && !s.IsDeleted);

                    if (slot == null)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            Message = "Không tìm thấy thông tin ô đỗ xe cố định của thẻ thành viên này."
                        };
                    }

                    if (slot.SlotStatus == ParkingStatuses.SlotOccupied)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            Message = $"Ô đỗ xe cố định {slot.SlotName} đã bị xe khác chiếm dụng."
                        };
                    }

                    // 5. Tạo một phiên đỗ xe mới lập tức (InProgress) cho thẻ thành viên
                    var newSession = new ParkingSession
                    {
                        UserId = membershipCard.UserId,
                        SlotId = slot.SlotId,
                        LicenseVehicle = cleanLicense ?? $"MBC_VEHICLE_{Guid.NewGuid().ToString().Substring(0, 5).ToUpper()}",
                        TypeId = membershipCard.Tier.TypeId,
                        CheckInTime = DateTime.UtcNow,
                        CheckInImageUrl = checkInImageUrl,
                        SessionStatus = ParkingStatuses.SessionInProgress,
                        TicketId = membershipCard.TicketId,
                        IsDeleted = false
                    };

                    // Chuyển trạng thái slot đỗ thành Occupied (đã đỗ)
                    slot.SlotStatus = ParkingStatuses.SlotOccupied;
                    _context.ParkingSlots.Update(slot);

                    await _context.ParkingSessions.AddAsync(newSession);
                    await _context.SaveChangesAsync();

                    _logger.LogInformation("Check-in THẺ THÀNH VIÊN thành công: Xe '{Plate}' vào bãi. Ô đỗ cố định: {SlotName}.",
                        newSession.LicenseVehicle, slot.SlotName);

                    return new ScanCheckInResponse
                    {
                        IsSuccess = true,
                        Message = $"Check-in thẻ thành viên thành công! Vui lòng đỗ xe vào vị trí đỗ cố định của bạn: {slot.SlotName}.",
                        SessionId = newSession.SessionId,
                        LicenseVehicle = newSession.LicenseVehicle,
                        SlotName = slot.SlotName,
                        VehicleTypeName = membershipCard.Tier.TypeId == 1 ? "Xe đạp" : (membershipCard.Tier.TypeId == 2 ? "Xe máy" : "Xe hơi"),
                        TicketCode = membershipCard.Ticket.TicketCode,
                        RequiresPayment = false,
                        PaymentStatus = "SUCCESS",
                        DriverName = membershipCard.User?.Username,
                        DriverPhone = membershipCard.User?.PhoneNumber,
                        DriverEmail = membershipCard.User?.Email
                    };
                }

                // Nếu không phải thẻ tháng, kiểm tra đặt chỗ (Reservation) bình thường
                if (int.TryParse(cleanTicketCode, out int ticketId))
                {
                    session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                }

                if (session != null && cleanLicense != null)
                {
                    if (!string.IsNullOrEmpty(session.LicenseVehicle))
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
                }
            }
            else if (cleanLicense != null)
            {
                session = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanLicense);
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
                if (session.Slot.SlotStatus == ParkingStatuses.SlotOccupied)
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
                TicketCode = session.Ticket?.TicketCode ?? cleanTicketCode,
                RequiresPayment = session.Invoice != null && session.Invoice.PaymentStatus == "PENDING",
                PaymentStatus = session.Invoice?.PaymentStatus ?? "NoPayment"
            };
        }

        public async Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request)
        {
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

        public async Task<ScanCheckInResponse> ScanQrCheckInAsync(string ticketCode, string? detectedPlate)
        {
            if (QrCodeParserHelper.TryParseQr(ticketCode, out var parsedTicket, out var parsedPlate, out var parsedSessionId, out var parsedSlot))
            {
                ticketCode = parsedTicket!;
                if (string.IsNullOrWhiteSpace(detectedPlate) || detectedPlate.Trim().ToLower() == "string")
                {
                    detectedPlate = parsedPlate;
                }
            }

            var isTicketCodeEmpty = string.IsNullOrWhiteSpace(ticketCode) ||
                                    ticketCode.Trim().ToLower() == "null" ||
                                    ticketCode.Trim().ToLower() == "undefined";

            if (isTicketCodeEmpty && string.IsNullOrWhiteSpace(detectedPlate))
            {
                return new ScanCheckInResponse { IsSuccess = false, Message = "Mã QR vé hoặc biển số xe không hợp lệ." };
            }

            string cleanTicketCode = isTicketCodeEmpty ? "" : ticketCode.Trim();

            // 1. Kiểm tra xem đây có phải mã vé thành viên hoạt động không
            if (!string.IsNullOrEmpty(cleanTicketCode))
            {
                var membershipCard = await _context.MembershipCards
                    .Include(mc => mc.Ticket)
                    .Include(mc => mc.Tier)
                    .Include(mc => mc.User)
                    .Include(mc => mc.Slot)
                    .Include(mc => mc.MembershipVehicles)
                    .FirstOrDefaultAsync(mc => mc.Ticket.TicketCode.Trim() == cleanTicketCode
                                         && mc.Status == ParkingStatuses.MonthlyCardActive
                                         && !mc.IsDeleted
                                         && mc.EndTime >= DateTime.UtcNow);

                if (membershipCard != null)
                {
                    // 1. Kiểm tra xem thẻ thành viên này có đang được xe khác dùng trong bãi hay không
                    var activeSession = await _context.ParkingSessions
                        .Include(s => s.Slot)
                        .FirstOrDefaultAsync(s => s.TicketId == membershipCard.TicketId
                                             && s.SessionStatus == ParkingStatuses.SessionInProgress
                                             && !s.IsDeleted);

                    if (activeSession != null)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            RequiresWalkIn = true,
                            Message = "Thẻ thành viên này hiện đang được sử dụng cho một xe khác trong bãi. Vui lòng chuyển sang hình thức vé vãng lai.",
                            SessionId = activeSession.SessionId,
                            LicenseVehicle = activeSession.LicenseVehicle,
                            SlotName = activeSession.Slot?.SlotName ?? "N/A",
                            VehicleTypeName = membershipCard.Tier.TypeId == 1 ? "Xe đạp" : (membershipCard.Tier.TypeId == 2 ? "Xe máy" : "Xe hơi"),
                            DriverName = membershipCard.User?.Username ?? "N/A",
                            DriverPhone = membershipCard.User?.PhoneNumber ?? "N/A",
                            DriverEmail = membershipCard.User?.Email ?? "N/A",
                            TicketCode = membershipCard.Ticket.TicketCode,
                            RequiresPayment = false,
                            PaymentStatus = "SUCCESS"
                        };
                    }

                    // 2. Kiểm tra xem biển số được phát hiện (detectedPlate) có đăng ký trong thẻ hay không (nếu có detectedPlate)
                    bool isPlateRegistered = true;
                    if (!string.IsNullOrWhiteSpace(detectedPlate) && detectedPlate.Trim().ToLower() != "string")
                    {
                        isPlateRegistered = membershipCard.MembershipVehicles
                            .Any(v => v.LicenseVehicle.Trim().ToUpper() == detectedPlate.Trim().ToUpper() && v.IsActive);
                    }

                    if (!isPlateRegistered)
                    {
                        return new ScanCheckInResponse
                        {
                            IsSuccess = false,
                            Message = $"Xe biển số '{detectedPlate}' không được đăng ký hoặc không hoạt động trong thẻ thành viên này.",
                            SessionId = 0,
                            LicenseVehicle = detectedPlate ?? "",
                            SlotName = "N/A",
                            VehicleTypeName = membershipCard.Tier.TypeId == 1 ? "Xe đạp" : (membershipCard.Tier.TypeId == 2 ? "Xe máy" : "Xe hơi"),
                            DriverName = membershipCard.User?.Username ?? "N/A",
                            DriverPhone = membershipCard.User?.PhoneNumber ?? "N/A",
                            DriverEmail = membershipCard.User?.Email ?? "N/A",
                            TicketCode = membershipCard.Ticket.TicketCode,
                            RequiresPayment = false,
                            PaymentStatus = "SUCCESS"
                        };
                    }

                    // 3. Nếu thành công:
                    return new ScanCheckInResponse
                    {
                        IsSuccess = true,
                        Message = $"Thẻ thành viên hợp lệ. Vui lòng đỗ xe vào vị trí đỗ cố định của bạn: {membershipCard.Slot?.SlotName ?? "N/A"}.",
                        SessionId = 0,
                        LicenseVehicle = detectedPlate ?? "",
                        SlotName = membershipCard.Slot?.SlotName ?? "N/A",
                        VehicleTypeName = membershipCard.Tier.TypeId == 1 ? "Xe đạp" : (membershipCard.Tier.TypeId == 2 ? "Xe máy" : "Xe hơi"),
                        DriverName = membershipCard.User?.Username ?? "N/A",
                        DriverPhone = membershipCard.User?.PhoneNumber ?? "N/A",
                        DriverEmail = membershipCard.User?.Email ?? "N/A",
                        TicketCode = membershipCard.Ticket.TicketCode,
                        RequiresPayment = false,
                        PaymentStatus = "SUCCESS"
                    };
                }
            }

            // Luồng xử lý vé đặt trước (Reservation) bình thường
            ParkingSession? session = null;
            if (isTicketCodeEmpty && !string.IsNullOrWhiteSpace(detectedPlate))
            {
                session = await _parkingRepository.GetReservedSessionByLicenseAsync(detectedPlate.Trim());
            }
            else
            {
                if (int.TryParse(cleanTicketCode, out int ticketId))
                {
                    session = await _parkingRepository.GetReservedSessionByTicketIdAsync(ticketId);
                }
                else
                {
                    session = await _parkingRepository.GetReservedSessionByTicketCodeAsync(cleanTicketCode);
                    if (session == null)
                    {
                        session = await _parkingRepository.GetReservedSessionByLicenseAsync(cleanTicketCode);
                    }
                }
            }

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
