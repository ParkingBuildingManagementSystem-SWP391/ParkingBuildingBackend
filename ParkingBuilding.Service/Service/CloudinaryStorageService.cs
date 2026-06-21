using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ParkingBuilding.Service.Helpers;
using ParkingBuilding.Service.IService;

namespace ParkingBuilding.Service.Service
{
    public class CloudinaryStorageService : IImageStorageService
    {
        private readonly Cloudinary _cloudinary;

        public CloudinaryStorageService(IOptions<CloudinarySettings> config)
        {
            var account = new Account(
                config.Value.CloudName,
                config.Value.ApiKey,
                config.Value.ApiSecret
            );
            _cloudinary = new Cloudinary(account);

            // Đảm bảo các URL do Cloudinary sinh ra luôn sử dụng giao thức bảo mật HTTPS
            _cloudinary.Api.Secure = true;
        }

        public async Task<string> UploadImageAsync(IFormFile file, string folderName)
        {
            if (file == null || file.Length == 0)
            {
                throw new ArgumentException("Tập tin hình ảnh không hợp lệ.");
            }

            // 1. Kiểm tra định dạng file (Chỉ cho phép tải ảnh)
            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".heic", ".heif", ".bmp" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                throw new ArgumentException("Định dạng file không được hỗ trợ. Chỉ chấp nhận file ảnh (.jpg, .jpeg, .png, .webp, .heic, .heif, .bmp).");
            }

            // 2. Giới hạn dung lượng file tối đa (Ví dụ: 5MB)
            const long maxFileSize = 5 * 1024 * 1024; // 5 Megabytes
            if (file.Length > maxFileSize)
            {
                throw new ArgumentException("Dung lượng file vượt quá giới hạn cho phép (tối đa 5MB).");
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var uploadParams = new ImageUploadParams()
            {
                File = new FileDescription(file.FileName, memoryStream),
                Folder = folderName
            };

            var uploadResult = await _cloudinary.UploadAsync(uploadParams);
            if (uploadResult.Error != null)
            {
                throw new Exception($"Lỗi upload Cloudinary: {uploadResult.Error.Message}");
            }

            // Tự động tối ưu chất lượng (q_auto) và định dạng nén (f_auto - tự động chuyển sang WebP/AVIF nếu trình duyệt hỗ trợ)
            return _cloudinary.Api.UrlImgUp
                .Transform(new Transformation().Quality("auto").FetchFormat("auto"))
                .BuildUrl(uploadResult.PublicId);
        }
    }
}
