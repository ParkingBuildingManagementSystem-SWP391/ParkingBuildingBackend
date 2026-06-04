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
    }
}