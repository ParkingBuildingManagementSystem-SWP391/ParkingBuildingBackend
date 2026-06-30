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

            decimal subFee = 0;
            if (hasDay) subFee += config.DayRate;
            if (hasNight) subFee += config.NightRate;

            return Math.Min(subFee, config.FullDayRate);
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

        // Tính số phút đỗ xe trong Ca Ngày (6h - 18h)
        private static double GetMinutesInDayShift(DateTime start, DateTime end)
        {
            start = NormalizeToUtc(start);
            end = NormalizeToUtc(end);

            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            double minutesInDay = 0;
            for (DateTime t = localStart; t < localEnd; t = t.AddMinutes(1))
            {
                int hour = t.Hour;
                if (hour >= DayShiftStart && hour < NightShiftStart)
                {
                    minutesInDay++;
                }
            }
            return minutesInDay;
        }

        // Tính số phút đỗ xe trong Ca Đêm (18h - 6h hôm sau)
        private static double GetMinutesInNightShift(DateTime start, DateTime end)
        {
            start = NormalizeToUtc(start);
            end = NormalizeToUtc(end);

            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            double minutesInNight = 0;
            for (DateTime t = localStart; t < localEnd; t = t.AddMinutes(1))
            {
                int hour = t.Hour;
                if (hour >= NightShiftStart || hour < DayShiftStart)
                {
                    minutesInNight++;
                }
            }
            return minutesInNight;
        }


    }

}
