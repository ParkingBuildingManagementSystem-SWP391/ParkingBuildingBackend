using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class MonthlyCardRegistrationResponseDto
    {
        public string Username { get; set; } = null!;
        public string TicketCode { get; set; } = null!;
        public decimal AmountToPay { get; set; }
        public DateTime EndTime { get; set; }
        public string PaymentUrl { get; set; } = null!;
    }
}
