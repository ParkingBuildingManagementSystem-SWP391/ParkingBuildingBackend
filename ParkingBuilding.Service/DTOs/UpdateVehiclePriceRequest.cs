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
        public decimal NewPrice { get; set; }
    }
}
