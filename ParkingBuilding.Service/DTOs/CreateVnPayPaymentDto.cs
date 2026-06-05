using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CreateVnPayPaymentDto
    {
        public long SessionId { get; set; }
        public string IpAddress { get; set; }
    }
}
