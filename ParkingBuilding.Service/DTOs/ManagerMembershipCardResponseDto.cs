using System;
using System.Collections.Generic;

namespace ParkingBuilding.Service.DTOs
{
    public class ManagerMembershipCardResponseDto
    {
        public int MembershipCardId { get; set; }
        public int UserId { get; set; }
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public int TierId { get; set; }
        public string? TierName { get; set; }
        public int? VehicleTypeId { get; set; }
        public string? VehicleTypeName { get; set; }
        public decimal Price { get; set; }
        public int DurationMonths { get; set; }
        public string? TicketCode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Status { get; set; } = null!;
        public bool IsDeleted { get; set; }
        public List<string> LicenseVehicles { get; set; } = new List<string>();
        public List<MembershipCardSlotDto> Slots { get; set; } = new List<MembershipCardSlotDto>();
    }

    public class MembershipCardSlotDto
    {
        public int SlotId { get; set; }
        public string? SlotName { get; set; }
        public string? SlotStatus { get; set; }
    }

    public class ManagerCancelMembershipCardResultDto
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; } = null!;
    }
}
