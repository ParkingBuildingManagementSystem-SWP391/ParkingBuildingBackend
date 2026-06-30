using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.ComponentModel.DataAnnotations;

namespace ParkingBuilding.Service.DTOs
{
    public class UpdateProfileRequest
    {
        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        public string Username { get; set; } = null!;

        [EmailAddress(ErrorMessage = "Định dạng Email không hợp lệ.")]
        public string? Email { get; set; }

        public string? PhoneNumber { get; set; }

        // Nếu người dùng muốn đổi mật khẩu thì điền 2 trường dưới đây
        public string? OldPassword { get; set; }
        public string? NewPassword { get; set; }
    }
}
