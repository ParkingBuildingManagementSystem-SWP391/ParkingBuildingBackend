using System;
using System.Text.Json.Serialization;

namespace ParkingBuilding.Service.Helpers
{
    public class AiPredictResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("license_plate")]
        public string LicensePlate { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("annotated_image")]
        public string? AnnotatedImage { get; set; }

        [JsonPropertyName("crop_image")]
        public string? CropImage { get; set; }

        public bool IsSuccess =>
            string.Equals(Status, "success", StringComparison.OrdinalIgnoreCase);
    }
}
