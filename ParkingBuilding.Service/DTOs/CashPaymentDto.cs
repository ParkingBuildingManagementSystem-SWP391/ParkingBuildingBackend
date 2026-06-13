using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CashPaymentDto
    {
        public string TicketCode { get; set; } = null!;
        public decimal AmountReceived { get; set; }
    }
}
