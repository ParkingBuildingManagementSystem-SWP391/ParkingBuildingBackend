using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service.Helpers
{
    public static class ParkingPricingCalculator
    {
        // Cấu hình mốc giờ ca cố định
        private const int DayShiftStart = 6;  // 6h sáng
        private const int NightShiftStart = 18; // 18h tối

        public static decimal CalculateFee(DateTime checkIn, DateTime checkOut, VehiclesType config)
        {
            if (checkOut <= checkIn) return 0;

            double totalHours = (checkOut - checkIn).TotalHours;

            // Tính số chu kỳ 24 giờ đầy đủ
            int fullDays = (int)Math.Floor(totalHours / 24);
            decimal fee = fullDays * config.FullDayRate;

            // Tính toán phần thời gian dư còn lại sau khi trừ các chu kỳ 24h
            DateTime remainingCheckIn = checkIn.AddDays(fullDays);
            decimal remainingFee = CalculateSubDayFee(remainingCheckIn, checkOut, config);

            fee += remainingFee;
            return fee;
        }

        private static decimal CalculateSubDayFee(DateTime start, DateTime end, VehiclesType config)
        {
            double durationHours = (end - start).TotalHours;
            if (durationHours <= 0) return 0;

            // 1. Tính số phút thực tế đỗ ở mỗi ca
            double minutesInDay = GetMinutesInDayShift(start, end);
            double minutesInNight = GetMinutesInNightShift(start, end);

            // 2. Chỉ tính phí ca đó nếu xe đỗ từ 30 phút trở lên
            bool hasDay = minutesInDay >= 30;
            bool hasNight = minutesInNight >= 30;

            // 3. Fallback: Nếu đỗ quá ngắn (dưới 30 phút), tính ca có thời gian đỗ nhiều nhất
            if (!hasDay && !hasNight)
            {
                if (minutesInDay >= minutesInNight)
                    hasDay = true;
                else
                    hasNight = true;
            }

            // ----------------------------------------------------
            // PHÂN LUỒNG LOGIC TÍNH GIÁ
            // ----------------------------------------------------
            if (config.TypeId == 1 || config.TypeId == 2)
            {
                // >>> LOGIC CŨ (Xe đạp, Xe máy): Cộng trọn gói cả ca
                decimal subFee = 0;
                if (hasDay) subFee += config.DayRate;
                if (hasNight) subFee += config.NightRate;

                return Math.Min(subFee, config.FullDayRate);
            }
            else
            {
                // >>> LOGIC MỚI (Xe hơi): Tính lũy tiến theo giờ thực tế + Chặn trần
                decimal calculatedHourlyFee = 0;
                double hoursToBill = Math.Ceiling(durationHours); // Làm tròn lên số giờ gửi

                if (hoursToBill > 0)
                {
                    // Cộng tiền giờ đầu tiên
                    calculatedHourlyFee += config.FirstHourRate;

                    // Cộng tiền các giờ tiếp theo nếu có
                    if (hoursToBill > 1)
                    {
                        calculatedHourlyFee += (decimal)(hoursToBill - 1) * config.SubsequentHourRate;
                    }
                }

                // Trần tối đa của xe hơi trong ca đỗ
                decimal maxAllowedCap = 0;
                if (hasDay && hasNight)
                {
                    maxAllowedCap = Math.Min(config.DayRate + config.NightRate, config.FullDayRate);
                }
                else if (hasDay)
                {
                    maxAllowedCap = config.DayRate;
                }
                else
                {
                    maxAllowedCap = config.NightRate;
                }

                // Trả về số tiền nhỏ hơn giữa tiền tính theo giờ thực tế và trần giới hạn
                return Math.Min(calculatedHourlyFee, maxAllowedCap);
            }
        }


        /// <summary>
        /// EF Core đọc DateTime từ SQL Server về với Kind = Unspecified.
        /// ConvertTimeFromUtc() yêu cầu Kind = Utc, nên phải normalize trước.
        /// Toàn bộ hệ thống lưu UTC nên SpecifyKind(Utc) là an toàn.
        /// </summary>
        private static DateTime NormalizeToUtc(DateTime dt)
        {
            if (dt.Kind == DateTimeKind.Unspecified)
                return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return dt;
        }

        // Tính số phút đỗ xe trong Ca Ngày (6h - 18h) bằng cách tính giao của interval
        // (hiệu năng O(số ngày), thay thế vòng lặp phút cũ)
        private static double GetMinutesInDayShift(DateTime start, DateTime end)
        {
            start = NormalizeToUtc(start);
            end   = NormalizeToUtc(end);

            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd   = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            double minutesInDay = 0;
            DateTime currentDay = localStart.Date;

            while (currentDay <= localEnd.Date)
            {
                // Ca ngày trong ngày hiện tại: [06:00, 18:00)
                DateTime shiftStart = currentDay.AddHours(DayShiftStart);
                DateTime shiftEnd   = currentDay.AddHours(NightShiftStart);

                // Phần giao của [localStart, localEnd) và [shiftStart, shiftEnd)
                DateTime overlapStart = localStart > shiftStart ? localStart : shiftStart;
                DateTime overlapEnd   = localEnd   < shiftEnd   ? localEnd   : shiftEnd;

                if (overlapEnd > overlapStart)
                    minutesInDay += (overlapEnd - overlapStart).TotalMinutes;

                currentDay = currentDay.AddDays(1);
            }

            return minutesInDay;
        }

        // Tính số phút đỗ xe trong Ca Đêm (18h - 6h hôm sau = [00:00,06:00) + [18:00,24:00))
        // (hiệu năng O(số ngày), thay thế vòng lặp phút cũ)
        private static double GetMinutesInNightShift(DateTime start, DateTime end)
        {
            start = NormalizeToUtc(start);
            end   = NormalizeToUtc(end);

            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd   = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            double minutesInNight = 0;
            DateTime currentDay = localStart.Date;

            while (currentDay <= localEnd.Date)
            {
                // Segment 1 trong ngày hiện tại: nửa đêm sớm [00:00, 06:00)
                DateTime seg1Start = currentDay;
                DateTime seg1End   = currentDay.AddHours(DayShiftStart);

                DateTime ov1Start = localStart > seg1Start ? localStart : seg1Start;
                DateTime ov1End   = localEnd   < seg1End   ? localEnd   : seg1End;
                if (ov1End > ov1Start)
                    minutesInNight += (ov1End - ov1Start).TotalMinutes;

                // Segment 2 trong ngày hiện tại: buổi tối [18:00, 24:00)
                DateTime seg2Start = currentDay.AddHours(NightShiftStart);
                DateTime seg2End   = currentDay.AddDays(1);

                DateTime ov2Start = localStart > seg2Start ? localStart : seg2Start;
                DateTime ov2End   = localEnd   < seg2End   ? localEnd   : seg2End;
                if (ov2End > ov2Start)
                    minutesInNight += (ov2End - ov2Start).TotalMinutes;

                currentDay = currentDay.AddDays(1);
            }

            return minutesInNight;
        }


    }

}
