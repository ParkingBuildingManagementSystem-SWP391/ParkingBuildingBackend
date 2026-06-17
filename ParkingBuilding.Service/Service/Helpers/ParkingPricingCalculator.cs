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

            // Trường hợp XE HƠI (Tính theo lượt tối đa 4 tiếng)
            if (config.MaxHoursPerTurn.HasValue && config.MaxHoursPerTurn.Value > 0)
            {
                int limit = config.MaxHoursPerTurn.Value;
                int numberOfTurns = (int)Math.Ceiling(durationHours / limit);

                // Chia khoảng thời gian thành các lượt nhỏ để xác định xem mỗi lượt thuộc ca nào
                decimal turnFee = 0;
                for (int i = 0; i < numberOfTurns; i++)
                {
                    DateTime turnStart = start.AddHours(i * limit);
                    DateTime turnEnd = (i == numberOfTurns - 1) ? end : start.AddHours((i + 1) * limit);

                    bool hasDayShift = OverlapsDayShift(turnStart, turnEnd);
                    bool hasNightShift = OverlapsNightShift(turnStart, turnEnd);

                    if (hasDayShift && hasNightShift)
                    {
                        // Lượt giao thoa ca
                        turnFee += Math.Min(config.DayRate + config.NightRate, config.FullDayRate);
                    }
                    else if (hasNightShift)
                    {
                        turnFee += config.NightRate;
                    }
                    else
                    {
                        turnFee += config.DayRate;
                    }
                }
                // Mức phí cho phần ngày lẻ này không được vượt quá giá trần 24h
                return Math.Min(turnFee, config.FullDayRate);
            }

            // Trường hợp XE MÁY / XE ĐẠP (Tính lũy tiến theo khung ca ngày/đêm)
            else
            {
                bool hasDay = OverlapsDayShift(start, end);
                bool hasNight = OverlapsNightShift(start, end);

                decimal subFee = 0;
                if (hasDay) subFee += config.DayRate;
                if (hasNight) subFee += config.NightRate;

                return Math.Min(subFee, config.FullDayRate);
            }
        }

        // Kiểm tra xem khoảng thời gian có giao thoa với Ca Ngày (6h - 18h) không
        private static bool OverlapsDayShift(DateTime start, DateTime end)
        {
            // Chuyển sang múi giờ địa phương Việt Nam để lấy Hour chính xác
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            // Quét từng giờ trong khoảng thời gian để kiểm tra
            for (DateTime t = localStart; t < localEnd; t = t.AddMinutes(15))
            {
                int hour = t.Hour;
                if (hour >= DayShiftStart && hour < NightShiftStart)
                {
                    return true;
                }
            }
            // Kiểm tra điểm cuối cùng
            int endHour = localEnd.Hour;
            if (endHour > DayShiftStart && endHour <= NightShiftStart && localEnd.Minute > 0)
            {
                return true;
            }

            return false;
        }

        // Kiểm tra xem khoảng thời gian có giao thoa với Ca Đêm (18h - 6h hôm sau) không
        private static bool OverlapsNightShift(DateTime start, DateTime end)
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime localStart = TimeZoneInfo.ConvertTimeFromUtc(start, tz);
            DateTime localEnd = TimeZoneInfo.ConvertTimeFromUtc(end, tz);

            for (DateTime t = localStart; t < localEnd; t = t.AddMinutes(15))
            {
                int hour = t.Hour;
                if (hour >= NightShiftStart || hour < DayShiftStart)
                {
                    return true;
                }
            }
            int endHour = localEnd.Hour;
            if ((endHour > NightShiftStart || endHour <= DayShiftStart) && localEnd.Minute > 0)
            {
                return true;
            }

            return false;
        }
    }

}
