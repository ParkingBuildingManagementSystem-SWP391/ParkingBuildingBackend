using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using ParkingBuilding.Service.Helpers;
using ParkingBuilding.Service.IService;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service
{
    public class FastApiLicensePlateService : IAiRecognitionService
    {
        private readonly HttpClient _httpClient;
        private readonly string _pythonAiUrl;

        public FastApiLicensePlateService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;

            var configuredUrl = configuration["AiSettings:PythonAiUrl"];
            _pythonAiUrl = !string.IsNullOrWhiteSpace(configuredUrl)
                ? configuredUrl
                : "https://vinhth-parking-license-ai.hf.space/predict-file-fast";
        }

        public async Task<string> PredictLicensePlateFromFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File ảnh không hợp lệ hoặc bị trống.");
            }

            byte[] fileBytes;
            using (var copyStream = new MemoryStream())
            {
                await file.CopyToAsync(copyStream);
                fileBytes = copyStream.ToArray();
            }

            return await PredictLicensePlateFromBytesAsync(fileBytes, file.ContentType, file.FileName);
        }

        public async Task<string> PredictLicensePlateFromBytesAsync(byte[] fileBytes, string? contentType, string? fileName)
        {
            if (fileBytes == null || fileBytes.Length == 0)
            {
                throw new ArgumentException("File ảnh không hợp lệ hoặc bị trống.");
            }

            var requestUrl = BuildRequestUrl();

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType);
            form.Add(fileContent, "file", string.IsNullOrWhiteSpace(fileName) ? "image.jpg" : fileName);

            using var response = await _httpClient.PostAsync(requestUrl, form);
            var rawText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Python AI trả lỗi HTTP {(int)response.StatusCode}: {rawText}");
            }

            try
            {
                var result = JsonSerializer.Deserialize<AiPredictResponse>(rawText);
                if (result != null)
                {
                    if (!result.IsSuccess)
                    {
                        throw new Exception(string.IsNullOrWhiteSpace(result.Message)
                            ? "Python AI nhận dạng không thành công."
                            : result.Message);
                    }

                    if (!string.IsNullOrWhiteSpace(result.LicensePlate))
                    {
                        return result.LicensePlate.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                if (!string.IsNullOrWhiteSpace(rawText))
                {
                    return rawText.Trim().Replace("\"", "");
                }
            }

            throw new Exception("Python AI không nhận diện được biển số từ file ảnh này.");
        }

        private string BuildRequestUrl()
        {
            var normalizedUrl = _pythonAiUrl.TrimEnd('/');

            if (normalizedUrl.EndsWith("/predict", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = normalizedUrl[..^"/predict".Length];
            }

            if (!normalizedUrl.EndsWith("/predict-file", StringComparison.OrdinalIgnoreCase) &&
                !normalizedUrl.EndsWith("/predict-file-fast", StringComparison.OrdinalIgnoreCase))
            {
                normalizedUrl = $"{normalizedUrl}/predict-file-fast";
            }

            return normalizedUrl;
        }
    }
}
