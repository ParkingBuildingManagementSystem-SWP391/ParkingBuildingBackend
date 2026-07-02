using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class UpdateMembershipTierPriceRequest
    {
        [Required(ErrorMessage = "Vui lòng nhập TypeId.")]
        [Range(1, int.MaxValue, ErrorMessage = "TypeId không hợp lệ.")]
        public int TypeId { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập số tháng (DurationMonths).")]
        [Range(1, 12, ErrorMessage = "Thời gian phải từ 1 đến 12 tháng.")]
        public int DurationMonths { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập giá tiền (Price).")]
        [Range(0, double.MaxValue, ErrorMessage = "Giá tiền không được âm.")]
        public decimal Price { get; set; }
    }
}
