using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using ParkingBuilding.Service.DTOs;
using ParkingBuilding.Service.IService;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class ManagerService : IManagerService
    {
        private readonly IManagerRepository _managerRepository;
        private readonly ParkingManagementDbContext _context;
        public ManagerService(IManagerRepository managerRepository, ParkingManagementDbContext context)
        {
            _managerRepository = managerRepository;
            _context = context;

        }

        public async Task<DashboardSummaryResponse> GetDashboardSummaryAsync()
        {
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, vnTimeZone);

            // 1. Tính mốc bắt đầu ngày hôm nay theo giờ Việt Nam
            var startOfTodayLocal = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 0, 0, 0);
            var startOfTodayUtc = TimeZoneInfo.ConvertTimeToUtc(startOfTodayLocal, vnTimeZone);


            // 3. Truy vấn dữ liệu thống kê từ Repository
            var totalSlots = await _managerRepository.GetTotalSlotsCountAsync();
            var occupiedSlots = await _managerRepository.GetOccupiedSlotsCountAsync();
            var reservedSlots = await _managerRepository.GetReservedSlotsCountAsync();
            var availableSlots = await _managerRepository.GetAvailableSlotsCountAsync();

            var vehiclesByType = await _managerRepository.GetVehiclesInBuildingDetailAsync();
            var floorOccupancy = await _managerRepository.GetFloorOccupancyDetailAsync();

            var todayRevenue = await _managerRepository.GetRevenueSinceAsync(startOfTodayUtc);
            var totalRevenue = await _managerRepository.GetTotalRevenueAsync();

            // 4. Ánh xạ kết quả trả về
            var response = new DashboardSummaryResponse
            {
                GeneratedTime = nowLocal,
                TotalSlotsCount = totalSlots,
                OccupiedSlotsCount = occupiedSlots,
                ReservedSlotsCount = reservedSlots,
                AvailableSlotsCount = availableSlots,
                OccupancyRate = totalSlots > 0 ? Math.Round((double)occupiedSlots / totalSlots * 100, 2) : 0,
                TodayRevenue = todayRevenue,
                TotalRevenue = totalRevenue,
                VehiclesInBuildingDetail = vehiclesByType.Select(v => new VehicleTypeCountDto
                {
                    VehicleTypeName = v.TypeName,
                    InBuildingCount = v.Count
                }).ToList(),
                FloorOccupancyDetail = floorOccupancy.Select(f => new FloorStatusDto
                {
                    FloorId = f.FloorId,
                    FloorName = f.FloorName,
                    Capacity = f.Capacity,
                    OccupiedCount = f.OccupiedCount
                }).ToList()
            };

            return response;
        }

        public async Task<List<TrafficStatsResponse>> GetTrafficStatsticsAsync(TrafficStatsRequest request)
        {
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

            // Lấy thời điểm bắt đầu ngày của StartDate và kết thúc ngày của EndDate theo múi giờ local Việt Nam
            var startDateLocal = new DateTime(request.StartDate.Year, request.StartDate.Month, request.StartDate.Day, 0, 0, 0);
            var endDateLocal = new DateTime(request.EndDate.Year, request.EndDate.Month, request.EndDate.Day, 23, 59, 59);

            var startDateUtc = TimeZoneInfo.ConvertTimeToUtc(startDateLocal, vnTimeZone);
            var endDateUtc = TimeZoneInfo.ConvertTimeToUtc(endDateLocal, vnTimeZone);

            var stats = await _managerRepository.GetTrafficStatsAsync(startDateUtc, endDateUtc, request.GroupBy, request.VehicleTypeId);

            return stats.Select(s => new TrafficStatsResponse
            {
                TimeLabel = s.TimeLabel,
                CheckInCount = s.CheckInCount,
                CheckOutCount = s.CheckOutCount,
                RevenueGenerated = s.Revenue
            }).ToList();
        }

        public async Task<ReportExportResponse> ExportReportAsync(ReportExportRequest request)
        {
            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            var startDateLocal = new DateTime(request.StartDate.Year, request.StartDate.Month, request.StartDate.Day, 0, 0, 0);
            var endDateLocal = new DateTime(request.EndDate.Year, request.EndDate.Month, request.EndDate.Day, 23, 59, 59);

            var startDateUtc = TimeZoneInfo.ConvertTimeToUtc(startDateLocal, vnTimeZone);
            var endDateUtc = TimeZoneInfo.ConvertTimeToUtc(endDateLocal, vnTimeZone);

            var data = await _managerRepository.GetParkingSessionsForExportAsync(startDateUtc, endDateUtc, request.VehicleTypeId);

            byte[] fileBytes;
            string contentType;
            string fileName;

            if (request.Format.ToUpper() == "PDF")
            {
                fileBytes = GeneratePdfReport(data, startDateLocal, endDateLocal);
                contentType = "application/pdf";
                fileName = $"BaoCao_LichSuDoXe_{startDateLocal:yyyyMMdd}_to_{endDateLocal:yyyyMMdd}.pdf";
            }
            else
            {
                fileBytes = GenerateExcelReport(data, startDateLocal, endDateLocal);
                contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
                fileName = $"BaoCao_LichSuDoXe_{startDateLocal:yyyyMMdd}_to_{endDateLocal:yyyyMMdd}.xlsx";
            }

            return new ReportExportResponse
            {
                FileBytes = fileBytes,
                ContentType = contentType,
                FileName = fileName
            };
        }

        public async Task<SlotDetailResponse?> GetSlotDetailAsync(int slotId)
        {
            var slot = await _managerRepository.GetSlotDetailWithActiveSessionAsync(slotId);
            if (slot == null) return null;

            var response = new SlotDetailResponse
            {
                SlotId = slot.SlotId,
                SlotName = slot.SlotName,
                SlotStatus = slot.SlotStatus,
                FloorName = slot.Floor?.FloorName ?? "N/A"
            };

            // Lấy session đang active của ô đỗ này (nếu có xe đỗ hoặc đã được đặt chỗ trước)
            var activeSession = slot.ParkingSessions
                .FirstOrDefault(ps => ps.SessionStatus == ParkingStatuses.SessionInProgress
                                     || ps.SessionStatus == ParkingStatuses.SessionReserved);

            if (activeSession != null)
            {
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");

                response.ActiveSession = new ActiveSessionDto
                {
                    SessionId = activeSession.SessionId,
                    LicenseVehicle = activeSession.LicenseVehicle,
                    VehicleTypeName = activeSession.Type?.TypeName ?? "N/A",
                    SessionStatus = activeSession.SessionStatus,
                    BookingTime = activeSession.BookingTime.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(activeSession.BookingTime.Value, vnTimeZone) : null,
                    CheckInTime = activeSession.CheckInTime.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(activeSession.CheckInTime.Value, vnTimeZone) : null
                };

                if (activeSession.User != null)
                {
                    response.ActiveSession.Customer = new CustomerInfoDto
                    {
                        UserId = activeSession.User.UserId,
                        Username = activeSession.User.Username,
                        Email = activeSession.User.Email,
                        PhoneNumber = activeSession.User.PhoneNumber,
                        CustomerType = activeSession.User.Role?.RoleName ?? "Đăng ký thành viên"
                    };
                }
            }

            return response;
        }

        // --- EXCEL EPPLUS GENERATOR ---
        private byte[] GenerateExcelReport(List<ParkingSession> data, DateTime start, DateTime end)
        {
            ExcelPackage.License.SetNonCommercialPersonal("SWP391");
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Bao_Cao_Lich_Su");

                // 1. Tiêu đề chính của báo cáo
                worksheet.Cells["A1:J1"].Merge = true;
                worksheet.Cells["A1"].Value = "BÁO CÁO CHI TIẾT LƯỢT XE RA VÀO VÀ DOANH THU";
                worksheet.Cells["A1"].Style.Font.Size = 16;
                worksheet.Cells["A1"].Style.Font.Bold = true;
                worksheet.Cells["A1"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                worksheet.Cells["A2:J2"].Merge = true;
                worksheet.Cells["A2"].Value = $"Thời gian xuất báo cáo: Từ {start:dd/MM/yyyy} đến {end:dd/MM/yyyy}";
                worksheet.Cells["A2"].Style.Font.Italic = true;
                worksheet.Cells["A2"].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                // 2. Tạo headers cho bảng
                string[] headers = {
                    "Mã Phiên", "Biển Số Xe", "Loại Xe", "Vị Trí Đỗ",
                    "Thời Gian Vào", "Thời Gian Ra", "Tổng Thời Gian (Giờ)",
                    "Nhân Viên Cổng", "Thành Tiền", "Trạng Thái"
                };

                for (int i = 0; i < headers.Length; i++)
                {
                    var cell = worksheet.Cells[4, i + 1];
                    cell.Value = headers[i];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(28, 54, 112)); // Màu Navy Blue
                    cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
                    cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    cell.Style.Border.BorderAround(ExcelBorderStyle.Thin);
                }

                // 3. Đổ dữ liệu hàng vào Excel
                var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                int rowIdx = 5;

                foreach (var item in data)
                {
                    worksheet.Cells[rowIdx, 1].Value = item.SessionId;
                    worksheet.Cells[rowIdx, 2].Value = item.LicenseVehicle;
                    worksheet.Cells[rowIdx, 3].Value = item.Type?.TypeName ?? "N/A";
                    worksheet.Cells[rowIdx, 4].Value = item.Slot?.SlotName ?? "N/A";

                    var localCheckIn = item.CheckInTime.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(item.CheckInTime.Value, vnTimeZone)
                        : (DateTime?)null;
                    var localCheckOut = item.CheckOutTime.HasValue
                        ? TimeZoneInfo.ConvertTimeFromUtc(item.CheckOutTime.Value, vnTimeZone)
                        : (DateTime?)null;

                    worksheet.Cells[rowIdx, 5].Value = localCheckIn?.ToString("dd/MM/yyyy HH:mm:ss") ?? "";
                    worksheet.Cells[rowIdx, 6].Value = localCheckOut?.ToString("dd/MM/yyyy HH:mm:ss") ?? "Trong bãi";

                    // Tính số giờ đỗ thực tế
                    double hours = 0;
                    if (item.CheckInTime.HasValue)
                    {
                        var endTime = item.CheckOutTime ?? DateTime.UtcNow;
                        hours = Math.Ceiling((endTime - item.CheckInTime.Value).TotalHours);
                    }
                    worksheet.Cells[rowIdx, 7].Value = hours;
                    worksheet.Cells[rowIdx, 8].Value = item.Invoice?.Staff?.Username ?? "Tự động/VNPAY";

                    // Định dạng số tiền tệ VNĐ
                    var amountCell = worksheet.Cells[rowIdx, 9];
                    amountCell.Value = item.Invoice?.TotalAmount ?? 0m;
                    amountCell.Style.Numberformat.Format = "#,##0\" VND\"";

                    worksheet.Cells[rowIdx, 10].Value = item.SessionStatus;

                    // Định dạng borders và căn lề
                    for (int col = 1; col <= headers.Length; col++)
                    {
                        worksheet.Cells[rowIdx, col].Style.Border.BorderAround(ExcelBorderStyle.Thin);
                    }
                    worksheet.Cells[rowIdx, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 2].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 3].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 4].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 5].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 6].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                    worksheet.Cells[rowIdx, 7].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    worksheet.Cells[rowIdx, 9].Style.HorizontalAlignment = ExcelHorizontalAlignment.Right;
                    worksheet.Cells[rowIdx, 10].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

                    rowIdx++;
                }

                // Tự động kéo giãn độ rộng cột khít dữ liệu
                worksheet.Cells[4, 1, rowIdx - 1, headers.Length].AutoFitColumns();

                return package.GetAsByteArray();
            }
        }

        // --- PDF QUESTPDF GENERATOR ---
        private byte[] GeneratePdfReport(List<ParkingSession> data, DateTime start, DateTime end)
        {
            // Cài đặt giấy phép phi thương mại cho QuestPDF
            QuestPDF.Settings.License = LicenseType.Community;

            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(1.5f, Unit.Centimetre);
                    page.Size(PageSizes.A4);
                    // Dùng font Arial hỗ trợ Unicode tiếng Việt tốt
                    page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

                    // --- HEADER ---
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("TÒA NHÀ PARKING SMART BUILDING").FontSize(12).Bold().FontColor(Colors.Blue.Darken3);
                            col.Item().Text("Bộ phận: Ban Quản lý Vận hành bãi đỗ").Italic().FontSize(9);
                        });
                        row.ConstantItem(120).Text("Mẫu báo cáo: BC-01/QL").AlignRight().FontSize(9);
                    });

                    // --- CONTENT ---
                    page.Content().PaddingVertical(0.8f, Unit.Centimetre).Column(col =>
                    {
                        col.Item().Text("BÁO CÁO LỊCH SỬ XE RA VÀO & DOANH THU").FontSize(16).Bold().AlignCenter();
                        col.Item().Text($"Thời gian báo cáo: Từ {start:dd/MM/yyyy} đến {end:dd/MM/yyyy}").AlignCenter().Italic().FontSize(10);
                        col.Item().PaddingTop(15);

                        // Vẽ bảng báo cáo
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30);  // STT
                                columns.RelativeColumn(2);   // Biển số xe
                                columns.RelativeColumn(2);   // Loại xe
                                columns.RelativeColumn(3);   // Giờ vào
                                columns.RelativeColumn(3);   // Giờ ra
                                columns.RelativeColumn(2);   // Tiền thu
                            });

                            // Headers
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("STT").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Biển Số").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Loại Xe").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Giờ Vào").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Giờ Ra").Bold();
                                header.Cell().Background(Colors.Grey.Lighten2).Padding(5).Text("Tiền Thu").Bold().AlignRight();
                            });

                            // Rows
                            var vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
                            int idx = 1;
                            foreach (var item in data)
                            {
                                var localCheckIn = item.CheckInTime.HasValue
                                    ? TimeZoneInfo.ConvertTimeFromUtc(item.CheckInTime.Value, vnTimeZone)
                                    : (DateTime?)null;
                                var localCheckOut = item.CheckOutTime.HasValue
                                    ? TimeZoneInfo.ConvertTimeFromUtc(item.CheckOutTime.Value, vnTimeZone)
                                    : (DateTime?)null;

                                table.Cell().Padding(5).Text(idx.ToString());
                                table.Cell().Padding(5).Text(item.LicenseVehicle);
                                table.Cell().Padding(5).Text(item.Type?.TypeName ?? "N/A");
                                table.Cell().Padding(5).Text(localCheckIn.HasValue ? $"{localCheckIn.Value:dd/MM/yyyy HH:mm}" : "N/A");
                                table.Cell().Padding(5).Text(localCheckOut.HasValue ? $"{localCheckOut.Value:dd/MM/yyyy HH:mm}" : "Trong bãi");

                                decimal totalAmount = item.Invoice?.TotalAmount ?? 0;
                                table.Cell().Padding(5).Text($"{totalAmount:N0} đ").AlignRight();

                                idx++;
                            }
                        });

                        // --- KÝ TÊN Ở CUỐI ---
                        col.Item().PaddingTop(30).Row(row =>
                        {
                            row.RelativeItem();
                            row.ConstantItem(180).Column(c =>
                            {
                                c.Item().Text($"Hà Nội, Ngày {DateTime.Now:dd} tháng {DateTime.Now:MM} năm {DateTime.Now:yyyy}").Italic().AlignCenter();
                                c.Item().PaddingTop(5);
                                c.Item().Text("Người lập báo cáo").Bold().AlignCenter();
                                c.Item().Text("(Ký và ghi rõ họ tên)").Italic().AlignCenter();
                                c.Item().PaddingTop(50);
                                c.Item().Text("BAN QUẢN LÝ TÒA NHÀ").Bold().AlignCenter();
                            });
                        });
                    });

                    // --- FOOTER ---
                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });

            using (var stream = new MemoryStream())
            {
                document.GeneratePdf(stream);
                return stream.ToArray();
            }
        }

        public async Task<bool> UpdateVehicleTypePricingAsync(
             int typeId,
             decimal dayRate,
             decimal nightRate,
             decimal fullDayRate,
             decimal monthlyPrice,
             decimal firstHourRate,
             decimal subsequentHourRate) 
        {
            var vehicleType = await _context.VehiclesTypes.FirstOrDefaultAsync(vt => vt.TypeId == typeId);
            if (vehicleType == null) return false;

            vehicleType.DayRate = dayRate;
            vehicleType.NightRate = nightRate;
            vehicleType.FullDayRate = fullDayRate;
            vehicleType.FirstHourRate = firstHourRate;
            vehicleType.SubsequentHourRate = subsequentHourRate;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<UpdateMembershipTierPriceResponse?> UpdateMembershipTierPricingAsync(UpdateMembershipTierPriceRequest request)
        {
            var tier = await _context.MembershipTiers
                .Include(t => t.Type)
                .FirstOrDefaultAsync(t => t.TypeId == request.TypeId 
                                     && t.DurationMonths == request.DurationMonths 
                                     && !t.IsDeleted);

            if (tier == null) return null;

            tier.Price = request.Price;
            await _context.SaveChangesAsync();

            return new UpdateMembershipTierPriceResponse
            {
                VehicleTypeName = tier.Type?.TypeName ?? "Unknown",
                DurationMonths = tier.DurationMonths,
                NewPrice = tier.Price,
                Message = "Cấu hình giá gói thành viên thành công!"
            };
        }
    }
}
