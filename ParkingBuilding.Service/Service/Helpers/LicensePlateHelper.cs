using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service.Helpers
{
    public class LicensePlateHelper
    {
        // 1. Regex cho xe dân sự và cơ quan nhà nước (ô tô, xe máy, cũ & mới)
        // Cấu trúc: Mã tỉnh (2 chữ số từ 11-99) + Sê-ri ký tự chữ (A-Z) + Ký tự sê-ri phụ tùy chọn (chữ A-Z hoặc số 0-9) + Số thứ tự (4 đến 6 chữ số)
        private static readonly Regex CivilPlateRegex = new Regex(@"^[1-9]\d[A-Z][A-Z0-9]?\d{4,6}$", RegexOptions.Compiled);

        // 2. Regex cho xe quân sự (biển đỏ)
        // Cấu trúc: 2 chữ số sê-ri quân binh chủng (A-Z) + Số thứ tự (4 đến 6 chữ số)
        private static readonly Regex MilitaryPlateRegex = new Regex(@"^[A-Z]{2}\d{4,6}$", RegexOptions.Compiled);

        /// <summary>
        /// Xác thực biển số xe Việt Nam và trả ra biển số đã được chuẩn hóa (viết hoa, không dấu/khoảng trắng).
        /// </summary>
        /// <param name="input">Biển số xe do người dùng/staff nhập</param>
        /// <param name="cleanedPlate">Biển số xe đã được chuẩn hóa nếu hợp lệ</param>
        /// <returns>True nếu hợp lệ, False nếu không hợp lệ</returns>
        public static bool IsValidLicensePlate(string? input, out string cleanedPlate)
        {
            cleanedPlate = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            // Loại bỏ tất cả khoảng trắng, dấu gạch ngang, dấu chấm và viết hoa toàn bộ
            string cleaned = Regex.Replace(input, @"[^A-Za-z0-9]", "").ToUpper();

            // So khớp với các mẫu Regex
            if (CivilPlateRegex.IsMatch(cleaned) || MilitaryPlateRegex.IsMatch(cleaned))
            {
                cleanedPlate = cleaned;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Thông báo lỗi hướng dẫn chuẩn hóa định dạng biển số xe.
        /// </summary>
        public static string GetErrorMessage()
        {
            return "Biển số xe không hợp lệ. Vui lòng nhập đúng định dạng biển số xe Việt Nam.\nMẫu chuẩn:\n- Ô tô: 29A-123.45 hoặc 30F-9999\n- Xe máy: 29-G1-123.45 hoặc 59-AA-123.45\n- Xe quân sự: AA-12-34";
        }
    }
}
