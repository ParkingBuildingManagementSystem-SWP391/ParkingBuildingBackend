using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public class CloudinaryUploadResult
    {
        public string RawUrl { get; set; }        // Dùng để lưu DB và gửi cho Python AI
        public string OptimizedUrl { get; set; }  // Dùng để trả về hiển thị mượt trên FE
        public string PublicId { get; set; }
    }

    public interface IImageStorageService
    {
        // Giữ nguyên hàm cũ để các chức năng khác không bị ảnh hưởng
        Task<string> UploadImageAsync(IFormFile file, string folderName);

        // Hàm mới trả về thông tin chi tiết
        Task<CloudinaryUploadResult> UploadImageDetailedAsync(IFormFile file, string folderName);
    }
}