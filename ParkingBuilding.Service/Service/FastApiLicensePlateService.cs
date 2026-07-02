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
            // Đọc cấu hình động từ appsettings.json, nếu trống thì fallback về url mặc định (predict-file)
            var configuredUrl = configuration["AiSettings:PythonAiUrl"];
            _pythonAiUrl = !string.IsNullOrWhiteSpace(configuredUrl)
                ? configuredUrl
                : "https://vinhth-parking-license-ai.hf.space/predict-file";
        }



        public async Task<string> PredictLicensePlateFromFileAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("File ảnh không hợp lệ hoặc bị trống.");
            }

            // ✅ FIX: Đọc toàn bộ nội dung file thành byte[] TRƯỚC khi tạo MultipartFormDataContent
            // Lý do: Nếu dùng StreamContent(file.OpenReadStream()), stream có thể bị Dispose bởi
            // caller (Controller) trước khi HttpClient gửi xong → Python AI nhận file rỗng.
            byte[] fileBytes;
            using (var copyStream = new MemoryStream())
            {
                await file.CopyToAsync(copyStream);
                fileBytes = copyStream.ToArray();
            }

            // Nếu cấu hình trỏ thẳng tới /predict-file thì dùng luôn, ngược lại chuyển đổi tự động
            string requestUrl = _pythonAiUrl;
            if (!requestUrl.EndsWith("/predict-file"))
            {
                if (requestUrl.EndsWith("/predict"))
                    requestUrl = requestUrl.Substring(0, requestUrl.Length - "/predict".Length);
                requestUrl = $"{requestUrl.TrimEnd('/')}/predict-file";
            }

            // Dùng ByteArrayContent thay cho StreamContent để tránh vấn đề stream bị đóng sớm
            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "image/jpeg");
            form.Add(fileContent, "file", file.FileName ?? "image.jpg");

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
                        throw new Exception(string.IsNullOrWhiteSpace(result.Message) ? "Python AI nhận dạng không thành công." : result.Message);
                    }
                    if (!string.IsNullOrWhiteSpace(result.LicensePlate))
                    {
                        return result.LicensePlate.Trim();
                    }
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