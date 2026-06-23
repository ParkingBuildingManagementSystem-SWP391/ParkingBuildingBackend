using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Helpers
{
    public class AiPredictRequest
    {
        [JsonPropertyName("image_url")]
        public string ImageUrl { get; set; } = string.Empty;
    }
    public class AiPredictResponse
    {
        [JsonPropertyName("license_plate")]
        public string LicensePlate { get; set; } = string.Empty;
    }
}
