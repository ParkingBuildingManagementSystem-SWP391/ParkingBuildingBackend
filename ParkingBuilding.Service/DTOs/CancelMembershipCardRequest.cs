using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class CancelMembershipCardRequest
    {
        [Required(ErrorMessage = "Vui lòng nhập CardId.")]
        public int CardId { get; set; }
    }
}
