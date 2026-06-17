using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class UpdateVehiclePriceRequest
    {
        public int VehicleTypeId { get; set; }
        public decimal DayRate { get; set; }
        public decimal NightRate { get; set; }
        public decimal FullDayRate { get; set; }
        public int? MaxHoursPerTurn { get; set; }
    }
}
