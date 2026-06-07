using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service.Helpers
{
    public static class PricingHelper
    {
        public static decimal GetHourlyRate(int vehicleTypeId)
        {
            return vehicleTypeId switch
            {
                2 => 5000m,   // Xe máy
                3 => 20000m,  // Ô tô
                _ => 2000m    // Xe đạp / Khác
            };
        }
    }
}
