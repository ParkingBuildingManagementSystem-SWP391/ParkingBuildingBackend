using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class ConfirmCheckInRequest
    {
        public string ImageUrl { get; set; }      // Raw URL nhận từ bước nhận diện
        public string ConfirmedPlate { get; set; } // Biển số cuối cùng sau khi kiểm tra
        public string? TicketCode { get; set; }   // Mã vé (nếu có)
        public int VehicleTypeId { get; set; }    // Loại xe
    }
}
