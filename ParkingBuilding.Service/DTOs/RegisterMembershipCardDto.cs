using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class RegisterMembershipCardDto
    {
        [Required(ErrorMessage = "Vui lòng chọn gói thành viên (TierId).")]
        public int TierId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số tháng muốn đăng ký.")]
        public int DurationMonths { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn ô đỗ xe (SlotId).")]
        public int SlotId { get; set; }

        [Required(ErrorMessage = "Vui lòng chọn loại xe (TypeId).")]
        public int TypeId { get; set; }

        [Required(ErrorMessage = "Vui lòng cung cấp ít nhất một biển số xe.")]
        public List<string> LicenseVehicles { get; set; } = new List<string>();
    }
}
