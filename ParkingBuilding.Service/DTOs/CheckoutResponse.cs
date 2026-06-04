using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutResponse
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = null!;

        public int SessionId { get; set; }
        public string TicketCode { get; set; } = null!;
        public string SlotName { get; set; } = null!;

        public string CheckInLicensePlate { get; set; } = null!;
        public string CheckOutLicensePlate { get; set; } = null!;
        public bool IsLicensePlateMatched { get; set; }

        public string? CheckInImageUrl { get; set; }
        public string? CheckOutImageUrl { get; set; }
        public DateTime CheckInTime { get; set; }
        public DateTime CheckOutTime { get; set; }
        public double DurationHours { get; set; }
        public decimal TotalAmount { get; set; }

        public int? InvoiceId { get; set; }
        public bool IsPaid { get; set; }

        public string StaffName { get; set; } = null!;
    }
}
