using System;

namespace ParkingBuilding.Service.DTOs
{
    public class CheckInRequest
    {
        public string? LicenseVehicle { get; set; }

        public string? TicketCode { get; set; }

        public string? CheckInImageUrl { get; set; }
    }
}