using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class BookSlotRequest
    {
        [Required]
        public int SlotId { get; set; }
        [Required]
        [DefaultValue("")]
        public string LicenseVehicle { get; set; } = null!;
        [Required]
        public int TypeId { get; set; }
        [Required]
        public DateTime ExpectedCheckInTime { get; set; } = DateTime.Now;// Thời gian xe dự kiến vào bãi

        public string PaymentMethod { get; set; } = "VNPAY"; // "VNPAY" hoáº·c "WALLET"
    }
}
