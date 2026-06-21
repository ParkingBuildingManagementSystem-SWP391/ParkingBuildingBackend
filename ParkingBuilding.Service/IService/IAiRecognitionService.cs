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
    }
}
