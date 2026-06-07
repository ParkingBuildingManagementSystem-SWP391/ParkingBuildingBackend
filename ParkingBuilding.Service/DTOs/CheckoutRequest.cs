using System;
using System.ComponentModel;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        [DefaultValue("")]
        public string? TicketCode { get; set; }

        public int? SessionId { get; set; }

        [DefaultValue("")]
        public string? CheckoutLicensePlate { get; set; }

        [DefaultValue("")]
        public string? CheckOutImageUrl { get; set; }

        [DefaultValue("CASH")]
        public string PaymentMethod { get; set; } = "CASH";
    }
}