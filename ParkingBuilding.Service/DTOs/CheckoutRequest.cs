using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        public string? TicketCode { get; set; }
        public int? SessionId { get; set; }

        public string CheckoutLicensePlate { get; set; } = null!;

        public string? CheckOutImageUrl { get; set; }

        public int StaffId { get; set; }
    }
}
