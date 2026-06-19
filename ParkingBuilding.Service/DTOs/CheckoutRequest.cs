using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckoutRequest
    {
        [DefaultValue("")]
        [Required]
        public string TicketCode { get; set; } = string.Empty;

        [DefaultValue("")]
        [Required]
        public string? CheckoutLicensePlate { get; set; }

        [DefaultValue("")]
        [Required]
        public string? CheckOutImageUrl { get; set; }

        [DefaultValue("CASH")]
        public string PaymentMethod { get; set; } = "CASH";
    }
}