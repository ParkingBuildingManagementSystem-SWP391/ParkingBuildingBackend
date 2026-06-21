using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IImageStorageService
    {
        /// <summary>
        /// Tải hình ảnh lên Cloudinary và trả về URL đã tối ưu hóa.
        /// </summary>
        Task<string> UploadImageAsync(IFormFile file, string folderName);
    }
}
