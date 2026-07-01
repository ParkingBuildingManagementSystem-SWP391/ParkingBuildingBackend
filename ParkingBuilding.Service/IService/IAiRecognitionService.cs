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
        /// Gửi URL hình ảnh sang dịch vụ AI để nhận dạng biển số xe.
        /// </summary>
        Task<string> PredictLicensePlateAsync(string imageUrl);

        /// <summary>
        /// Gửi trực tiếp file hình ảnh sang dịch vụ AI (multipart/form-data) để nhận dạng biển số xe.
        /// </summary>
        Task<string> PredictLicensePlateFromFileAsync(IFormFile file);
    }
}
