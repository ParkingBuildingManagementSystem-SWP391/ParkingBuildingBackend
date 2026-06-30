using System;
using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class RegisterMonthlyCardDto
    {
        [Required(ErrorMessage = "Vui lòng chọn gói cước (TariffId).")]
        [Range(1, 3, ErrorMessage = "TariffId không hợp lệ. 1: Xe đạp, 2: Xe máy, 3: Xe hơi.")]
        public int TariffId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số tháng muốn thuê.")]
        [Range(1, 12, ErrorMessage = "Thời hạn thuê phải từ 1 đến 12 tháng.")]
        public int DurationMonths { get; set; }
    }
}
