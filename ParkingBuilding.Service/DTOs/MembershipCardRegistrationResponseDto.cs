using System;
using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class MembershipCardRegistrationResponseDto
    {
        public string Username { get; set; } = null!;
        public string TicketCode { get; set; } = null!;
        public decimal AmountToPay { get; set; }
        public int SlotId { get; set; }
        public List<string> LicenseVehicles { get; set; } = new List<string>();
        public DateTime EndTime { get; set; }
        public string PaymentUrl { get; set; } = null!;
    }
}
