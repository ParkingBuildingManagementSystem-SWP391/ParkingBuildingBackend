using Microsoft.AspNetCore.Http;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using ParkingBuilding.Service.Helpers;
using ParkingBuilding.Service.IService;

namespace ParkingBuilding.Service.Service
{
    public class FastApiLicensePlateService : IAiRecognitionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _pythonAiUrl;

        public FastApiLicensePlateService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            // Đọc cấu hình động từ appsettings.json
            _pythonAiUrl = configuration["AiSettings:PythonAiUrl"]
                           ?? "https://vinhth-parking-license-ai.hf.space/predict";
        }

        public async Task<string> PredictLicensePlateAsync(string imageUrl)
        {
            var response = await _httpClient.PostAsJsonAsync(
                _pythonAiUrl,
                new AiPredictRequest { ImageUrl = imageUrl }
            );

            // ĐỌC RAW STRING TRƯỚC — HTTP response stream chỉ đọc được 1 lần duy nhất
            // Nếu đọc JSON trước rồi mới đọc string sẽ bị lỗi stream đã bị consumed
            var rawText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Python AI trả lỗi HTTP {(int)response.StatusCode}: {rawText}");
            }

            // Parse JSON từ rawText đã lưu sẵn
            // Python trả về: { "status": "success", "license_plate": "30F455775" }
            try
            {
                var result = JsonSerializer.Deserialize<AiPredictResponse>(rawText);
                if (result != null && !string.IsNullOrWhiteSpace(result.LicensePlate))
                {
                    return result.LicensePlate.Trim();
                }
            }
            catch (JsonException)
            {
                // Trường hợp Python trả về plain string thay vì JSON object
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    return rawText.Trim().Replace("\"", "");
                }
            }

            throw new Exception("Python AI không nhận diện được biển số từ ảnh này.");
        }

        public async Task<string> PredictLicensePlateFromFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File ảnh không hợp lệ hoặc bị trống.");
            }

            using var form = new MultipartFormDataContent();
            using var stream = file.OpenReadStream();
            using var fileContent = new StreamContent(stream);

            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "image/jpeg");
            form.Add(fileContent, "file", file.FileName);

            string baseUrl = _pythonAiUrl;
            if (baseUrl.EndsWith("/predict"))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - "/predict".Length);
            }
            string requestUrl = $"{baseUrl.TrimEnd('/')}/predict-file";

            using var response = await _httpClient.PostAsync(requestUrl, form);
            var rawText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Python AI trả lỗi HTTP {(int)response.StatusCode}: {rawText}");
            }

            try
            {
                var result = JsonSerializer.Deserialize<AiPredictResponse>(rawText);
                if (result != null && !string.IsNullOrWhiteSpace(result.LicensePlate))
                {
                    return result.LicensePlate.Trim();
                }
            }
            catch (JsonException)
            {
                // Trường hợp Python trả về plain string thay vì JSON object
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    return rawText.Trim().Replace("\"", "");
                }
            }

            throw new Exception("Python AI không nhận diện được biển số từ file ảnh này.");
        }
    }
}