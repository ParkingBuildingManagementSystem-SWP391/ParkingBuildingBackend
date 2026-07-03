using System;
using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class MembershipCardRegistrationResponseDto
    {
        // === Backward compatible fields (giữ nguyên) ===
        public string Username { get; set; } = null!;
        public string TicketCode { get; set; } = null!;

        public decimal AmountToPay { get; set; }


        public int SlotId { get; set; }

        public List<string> LicenseVehicles { get; set; } = new List<string>();
        public DateTime EndTime { get; set; }
        public string? PaymentUrl { get; set; }
        public long? InvoiceId { get; set; }
        public string? PaymentStatus { get; set; }
        public string? PaymentMethod { get; set; }
        public string? TransactionCode { get; set; }

        // === Các field mới - đầy đủ thông tin ===

        public List<string> TicketCodes { get; set; } = new List<string>();


        public List<int> SlotIds { get; set; } = new List<int>();


        public List<string> SlotNames { get; set; } = new List<string>();


        public DateTime StartTime { get; set; }
    }
}
