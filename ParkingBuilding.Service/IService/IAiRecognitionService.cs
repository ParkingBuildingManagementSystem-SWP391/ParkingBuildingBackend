using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IAiRecognitionService
    {
        /// <summary>
        /// Gửi trực tiếp file hình ảnh sang dịch vụ AI (multipart/form-data) để nhận dạng biển số xe.
        /// </summary>
        Task<string> PredictLicensePlateFromFileAsync(IFormFile file);

        /// <summary>
        /// Gửi nội dung ảnh đã đọc sẵn sang dịch vụ AI, tránh copy lại file khi caller đã có byte[].
        /// </summary>
        Task<string> PredictLicensePlateFromBytesAsync(byte[] fileBytes, string? contentType, string? fileName);
    }
}
