using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class RegisterMembershipCardDto
    {
        [Required(ErrorMessage = "Vui lòng chọn gói thành viên (TierId).")]
        public int TierId { get; set; }

        public int? SlotId { get; set; }

        public List<int> SlotIds { get; set; } = new List<int>();

        [Required(ErrorMessage = "Vui lòng cung cấp ít nhất một biển số xe.")]
        public List<string> LicenseVehicles { get; set; } = new List<string>();
    }
}
