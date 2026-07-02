# Tài liệu Nghiệp vụ & Kỹ thuật: Phân hệ Đăng ký Thẻ thành viên (Membership Card)

Tài liệu này ghi lại chi tiết toàn bộ các chỉnh sửa, file thêm mới, các logic nghiệp vụ và cấu trúc hoạt động của hệ thống sau khi loại bỏ hoàn toàn phân hệ Thẻ tháng (Monthly Card) cũ và tích hợp phân hệ Thẻ thành viên (Membership Card) mới.

---

## I. Tóm tắt các thay đổi đã thực hiện

1. **Loại bỏ hoàn toàn Monthly Card**: Xóa bỏ tất cả thực thể (`MonthlyCard`, `MonthlyTariff`), DTO, Service (`MonthlyCardService`), Controller (`MonthlyCardController`), và Background Service liên quan đến Thẻ tháng.
2. **Cập nhật Check-In & Check-Out**: Thay thế hoàn toàn kịch bản xử lý thẻ tháng sang thẻ thành viên mới:
   - **Khi Check-In**: Đối khớp vé thành viên và xác thực biển số xe thực tế đi vào cổng phải trùng với một trong các biển số đang hoạt động được đăng ký trong thẻ thành viên. Phân bổ đỗ tại ô đỗ cố định của thành viên (`Reserved` -> `Occupied`).
   - **Khi Check-Out**: Ghi nhận hóa đơn đỗ bằng 0 VNĐ và khóa lại ô đỗ cố định của thành viên ở trạng thái `Reserved` (không giải phóng thành `Available` để đảm bảo ô đỗ đỗ xe luôn thuộc quyền sở hữu riêng của thành viên).
3. **Cập nhật IPN Webhook VNPay**: Cấu hình webhook thanh toán nhận dạng mã giao dịch thẻ thành viên bắt đầu bằng tiền tố `"MBC_"`, giải quyết và tự động kích hoạt thẻ thành viên sau khi nhận tín hiệu giao dịch thành công.
4. **Xây dựng Background Service mới**: Tiến trình chạy ngầm quét 1 phút/lần tự động khóa các thẻ thành viên hết hạn sử dụng (chuyển sang `Suspended`, vô hiệu hóa các biển số xe liên quan, mở khóa ô đỗ xe về `Available`) và hoàn tác/giải phóng ô đỗ của các giao dịch VNPay hết hạn thanh toán (quá 15 phút).
5. **Cải tiến DTO & Tự động phân giải thông tin**: Loại bỏ các trường `DurationMonths` và `TypeId` khỏi payload đầu vào của API đăng ký. Hệ thống tự động truy vấn hai giá trị này trực tiếp từ thực thể `MembershipTier` trong database thông qua `TierId` để tránh dữ liệu dư thừa và sai sót đầu vào.

---

## II. Danh sách các file & Vị trí chỉnh sửa cụ thể

### 1. Các file đã xóa hoàn toàn (Clean up)
- `ParkingBuilding.Repository/Entities/MonthlyCard.cs`
- `ParkingBuilding.Repository/Entities/MonthlyTariff.cs`
- `ParkingBuilding.Service/DTOs/MonthlyCardRegistrationResponseDto.cs`
- `ParkingBuilding.Service/DTOs/RegisterMonthlyCardDto.cs`
- `ParkingBuilding.Service/IService/IMonthlyCardService.cs`
- `ParkingBuilding.Service/Service/MonthlyCardService.cs`
- `ParkingBuilding.API/Controllers/MonthlyCardController.cs`
- `ParkingBuilding.API/BackgroundServices/MonthlyCardExpirationProcessor.cs`

---

### 2. Các file thêm mới (New additions)

#### 2.1. DTOs (Data Transfer Objects)
- **[RegisterMembershipCardDto.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/DTOs/RegisterMembershipCardDto.cs)**
  - *Công dụng*: Nhận thông tin đăng ký từ Driver.
  - *Cấu trúc*:
    - `TierId` (int, Required): ID gói thành viên cần đăng ký.
    - `SlotId` (int, Required): ID ô đỗ xe cố định khách hàng muốn chọn.
    - `LicenseVehicles` (List<string>, Required): Danh sách biển số xe đăng ký (1 tháng tối đa 1 xe, 6 tháng tối đa 2 xe, 12 tháng tối đa 3 xe).
- **[MembershipCardRegistrationResponseDto.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/DTOs/MembershipCardRegistrationResponseDto.cs)**
  - *Công dụng*: Trả về kết quả và link thanh toán VNPay.
  - *Cấu trúc*:
    - `Username` (string): Tên tài xế.
    - `TicketCode` (string): Mã vé duy nhất được sinh ra cho thẻ thành viên.
    - `AmountToPay` (decimal): Số tiền cần thanh toán.
    - `SlotId` (int): Ô đỗ xe cố định đã chọn.
    - `LicenseVehicles` (List<string>): Các biển số xe đã được chuẩn hóa.
    - `EndTime` (DateTime): Thời gian hết hạn dự kiến.
    - `PaymentUrl` (string): Đường dẫn thanh toán VNPay.

#### 2.2. Service Layer
- **[IMembershipCardService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/IService/IMembershipCardService.cs)**
  - *Công dụng*: Khai báo các phương thức nghiệp vụ quản lý thẻ thành viên.
- **[MembershipCardService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/Service/MembershipCardService.cs)**
  - *Công dụng*: Xử lý logic nghiệp vụ đăng ký và thanh toán thẻ.
  - *Chi tiết các hàm*:
    - `RegisterMembershipCardAsync(int userId, RegisterMembershipCardDto dto, string ipAddress)`:
      - Kiểm tra User, TierId và SlotId trong DB.
      - Phân giải `DurationMonths`, `TypeId`, `Price`, `MaxVehicles` trực tiếp từ bảng `MembershipTiers`.
      - Kiểm tra số lượng biển số xe truyền vào không vượt quá `tier.MaxVehicles`.
      - Chuẩn hóa định dạng biển số xe bằng `LicensePlateHelper.IsValidLicensePlate` (bỏ qua nếu loại xe là Xe đạp - TypeId = 1).
      - Đánh dấu ô đỗ xe thành `Reserved` để khóa ô đỗ tạm thời.
      - Tạo hóa đơn `Invoice` với trạng thái `PENDING` và lưu thông tin đăng ký tạm vào `IMemoryCache` trong 15 phút.
      - Sinh mã giao dịch chứa SlotId: `MBC_{SlotId}_{Ticks}`.
      - Tạo và trả về link thanh toán VNPay.
    - `ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus)`:
      - Xử lý xác nhận thanh toán từ IPN Webhook của VNPay, bọc trong **Database Transaction**.
      - Nếu thất bại: Đổi hóa đơn sang `FAILED`, phân tích cú pháp để lấy `SlotId` từ mã giao dịch và trả ô đỗ về trạng thái `Available`.
      - Nếu thành công: 
        - Lấy thông tin tạm từ Cache. Nếu cache hết hạn (quá 15 phút), giải phóng ô đỗ và đánh dấu Invoice thất bại.
        - Nếu cache hợp lệ: Tạo thực thể `Ticket` (trạng thái `Active`), tạo `MembershipCard` (mốc `StartTime` tính từ lúc thanh toán thành công, `EndTime = StartTime + DurationMonths`, trạng thái `Active`), tạo danh sách xe trong `MembershipVehicles` (trạng thái `IsActive = true`), đổi `Invoice` sang `SUCCESS`.
        - Xóa thông tin cache và commit transaction.
    - `GetMyActiveCardAsync(int userId)`: Truy vấn thẻ thành viên đang hoạt động của tài xế kèm danh sách biển số xe.
    - `GetActiveTiersAsync()`: Lấy danh sách các gói cước thành viên đang hoạt động trong hệ thống.

#### 2.3. Controller Layer
- **[MembershipCardController.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/Controllers/MembershipCardController.cs)**
  - *Công dụng*: Cung cấp API endpoints cho tài xế đăng ký thẻ thành viên.
  - *Bảo mật*: Giới hạn quyền truy cập chỉ dành cho role `Registered_Driver` bằng `[Authorize(Roles = "Registered_Driver")]`.
  - *Endpoints*:
    - `POST api/membershipcard/register`: Đăng ký và nhận URL thanh toán VNPay.
    - `GET api/membershipcard/my-card`: Lấy thông tin thẻ thành viên hiện tại của tài xế.
    - `GET api/membershipcard/tiers`: Lấy danh sách gói cước.

#### 2.4. Background Service (Tiến trình chạy ngầm)
- **[MembershipCardExpirationProcessor.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/BackgroundServices/MembershipCardExpirationProcessor.cs)**
  - *Công dụng*: Chạy ngầm định kỳ 1 phút/lần.
  - *Nhiệm vụ 1*: Tự động quét và khóa thẻ thành viên đã hết hạn sử dụng (`EndTime < localNow`):
    - Đổi trạng thái thẻ sang `Expired`.
    - Trả ô đỗ cố định của thẻ về trạng thái `Available`.
    - Cập nhật toàn bộ xe trong `MembershipVehicles` liên kết sang `IsActive = false`.
  - *Nhiệm vụ 2*: Tự động giải phóng ô đỗ từ các giao dịch VNPay đăng ký membership PENDING đã quá 15 phút mà không thanh toán thành công.
    - Chuyển `Invoice` sang `FAILED`.
    - Parse `SlotId` từ mã giao dịch để hoàn tác trạng thái ô đỗ về `Available`.

---

### 3. Các file được chỉnh sửa (Modifications)

#### 3.1. [Program.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/Program.cs)
- Gỡ bỏ cấu hình dịch vụ Thẻ tháng.
- Đăng ký `MembershipCardExpirationProcessor` làm hosted service và `IMembershipCardService` làm scoped service.

#### 3.2. [PaymentsController.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/Controllers/PaymentsController.cs)
- Thay đổi logic IPN Webhook: Nhận diện mã giao dịch bắt đầu bằng tiền tố `"MBC_"`.
- Gọi dịch vụ `IMembershipCardService.ConfirmMembershipCardPaymentAsync` để xử lý thanh toán và kích hoạt thẻ thành viên.

#### 3.3. [ManagerController.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/Controllers/ManagerController.cs)
- Gỡ bỏ 2 API quản trị thẻ tháng: `CancelMonthlyCard` và `GetAllMonthlyCards`.

#### 3.4. [IManagerService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/IService/IManagerService.cs) & [ManagerService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/Service/ManagerService.cs)
- Xóa bỏ định nghĩa và hiện thực các hàm quản trị thẻ tháng.
- Loại bỏ logic cập nhật giá thẻ tháng (`MonthlyTariff`) trong hàm cập nhật biểu phí `UpdateVehicleTypePricingAsync`.

#### 3.5. [CheckInService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/Service/CheckInService.cs)
- Thay đổi toàn bộ logic check-in bằng thẻ thành viên (áp dụng cho cả check-in thủ công tại quầy và check-in tự động quét mã QR):
  - Kiểm tra xem TicketCode có thuộc thẻ thành viên `Active` và còn hạn hay không.
  - Kiểm tra xem biển số xe đi vào thực tế có nằm trong danh sách biển số đang hoạt động (`IsActive = true`) của thẻ này không.
  - Phân bổ đỗ xe đúng tại ô đỗ cố định (`SlotId`) đã đăng ký trên thẻ. Chuyển trạng thái ô đỗ thành `Occupied`.
  - Khởi tạo phiên đỗ xe `ParkingSession` trạng thái `InProgress`.

#### 3.6. [CheckOutService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/Service/CheckOutService.cs)
- Thay đổi logic check-out bằng thẻ thành viên (cho cả check-out thủ công và tự động):
  - Tìm thẻ thành viên đang hoạt động thông qua `TicketId` của phiên đỗ.
  - Ghi nhận hóa đơn thanh toán thành công với số tiền 0 VNĐ và phương thức thanh toán là `MEMBERSHIP_CARD`.
  - Khóa ô đỗ xe cố định: Cập nhật trạng thái ô đỗ về `Reserved` thay vì `Available` để giữ chỗ độc quyền cho thành viên.

---

## III. Cấu hình Giá gói thành viên dành cho Manager (Mới thêm)

Nhằm cho phép Manager cập nhật biểu phí của các gói thành viên linh hoạt dựa trên loại xe (`TypeId`) và thời gian tháng đăng ký (`DurationMonths`), hệ thống đã được bổ sung thêm một API cấu hình giá.

### 1. DTOs cấu hình
- **[UpdateMembershipTierPriceRequest.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/DTOs/UpdateMembershipTierPriceRequest.cs)**:
  - *Công dụng*: Nhận thông tin cấu hình biểu phí từ Manager.
  - *Thuộc tính*:
    - `TypeId` (int): ID loại xe (1: Xe đạp, 2: Xe máy, 3: Xe hơi).
    - `DurationMonths` (int): Thời gian gói (1, 6, 12 tháng).
    - `Price` (decimal): Mức giá mới áp dụng.
- **[UpdateMembershipTierPriceResponse.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/DTOs/UpdateMembershipTierPriceResponse.cs)**:
  - *Công dụng*: Trả về xác nhận kết quả cập nhật giá.
  - *Thuộc tính*:
    - `VehicleTypeName` (string): Tên loại xe (ví dụ: Xe hơi).
    - `DurationMonths` (int): Thời hạn tháng của gói.
    - `NewPrice` (decimal): Giá mới đã được cập nhật thành công.
    - `Message` (string): Thông điệp xác nhận thành công.

### 2. Service Layer
- **[IManagerService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/IService/IManagerService.cs)**:
  - Đăng ký signature: `Task<UpdateMembershipTierPriceResponse?> UpdateMembershipTierPricingAsync(UpdateMembershipTierPriceRequest request);`
- **[ManagerService.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.Service/Service/ManagerService.cs)**:
  - Hàm `UpdateMembershipTierPricingAsync`:
    - Truy vấn gói thành viên `MembershipTier` từ DB khớp với `TypeId` và `DurationMonths` và chưa bị xóa.
    - Nếu không tìm thấy, trả về `null` (để controller trả lỗi `404 Not Found`).
    - Nếu tìm thấy, thực hiện cập nhật trường `Price` bằng giá trị `request.Price` mới, lưu thay đổi vào DB.
    - Trả về thông tin loại xe, số tháng, mức giá mới và thông điệp xác nhận.

### 3. Controller Layer
- **[ManagerController.cs](file:///d:/FPT-SU26/SWP391/ParkingBuildingBE/ParkingBuildingBackend/ParkingBuilding.API/Controllers/ManagerController.cs)**:
  - Endpoint: `PUT api/manager/update-membership-pricing`
  - Quyền truy cập: Chỉ role `Manager` (được bảo vệ bởi attribute `[Authorize(Roles = "Manager")]`).
  - Logic: 
    - Nhận dữ liệu `UpdateMembershipTierPriceRequest` từ body.
    - Gọi hàm nghiệp vụ trong `ManagerService` để cập nhật.
    - Nếu thành công, trả về status `200 OK` kèm theo object chi tiết loại xe, số tháng và mức giá mới.
    - Nếu không tìm thấy gói, trả về `404 Not Found` kèm thông báo lỗi.

