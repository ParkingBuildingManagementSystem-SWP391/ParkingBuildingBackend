using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class VnPayIpnRequestDto
    {
        public string? vnp_TxnRef { get; set; }
        public string? vnp_Amount { get; set; }
        public string? vnp_ResponseCode { get; set; }
        public string? vnp_TransactionStatus { get; set; }
        public string? vnp_SecureHash { get; set; }
    }
}
