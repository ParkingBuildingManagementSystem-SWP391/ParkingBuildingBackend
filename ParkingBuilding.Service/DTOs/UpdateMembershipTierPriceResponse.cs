namespace ParkingBuilding.Service.DTOs
{
    public class UpdateMembershipTierPriceResponse
    {
        public string VehicleTypeName { get; set; } = null!;
        public int DurationMonths { get; set; }
        public decimal NewPrice { get; set; }
        public string Message { get; set; } = null!;
    }
}
