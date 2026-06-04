using System;
using System.ComponentModel;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckInRequest
    {
        [DefaultValue("")]
        public string? LicenseVehicle { get; set; }

        [DefaultValue("")]
        public string? TicketCode { get; set; }

        [DefaultValue("")]
        public string? CheckInImageUrl { get; set; }
    }
}