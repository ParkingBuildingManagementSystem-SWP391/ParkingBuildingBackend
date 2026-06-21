using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    using ParkingBuilding.Service.Helpers;
    using ParkingBuilding.Service.IService;
    using System.Net.Http.Json;

    public class FastApiLicensePlateService : IAiRecognitionService
    {
        private readonly HttpClient _httpClient;
        public FastApiLicensePlateService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<string> PredictLicensePlateAsync(string imageUrl)
        {
            var response = await _httpClient.PostAsJsonAsync("http://localhost:8000/predict", new AiPredictRequest { ImageUrl = imageUrl });

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Lỗi kết nối FastAPI AI. Code: {response.StatusCode}");
            }
            var result = await response.Content.ReadFromJsonAsync<AiPredictResponse>();
            if (result == null || string.IsNullOrWhiteSpace(result.LicensePlate))
            {
                var rawText = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    return rawText.Trim().Replace("\"", "");
                }
                throw new Exception("Không nhận diện được biển số từ ảnh.");
            }
            return result.LicensePlate;
        }
    }
}
