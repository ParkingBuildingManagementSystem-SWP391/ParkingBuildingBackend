using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class UpdateMembershipVehiclesRequest
    {
        [Required(ErrorMessage = "Vui lòng nhập CardId.")]
        public int CardId { get; set; }

        [Required(ErrorMessage = "Vui lòng cung cấp danh sách biển số xe.")]
        public List<string> LicenseVehicles { get; set; } = new List<string>();
    }
}
