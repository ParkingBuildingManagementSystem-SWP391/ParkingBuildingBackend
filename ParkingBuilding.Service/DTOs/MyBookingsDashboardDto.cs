using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class MyBookingsDashboardDto
    {
        public int TotalBookings { get; set; }
        public int ActiveBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int CanceledBookings { get; set; }
        public decimal TotalAmountSpent { get; set; }
        public List<MyBookingResponseDto> BookingsList { get; set; } = new();
    }
}
