using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{

    public class RegisterTempData
    {
        public string Email { get; set; } = null!;
        public string PasswordHash { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string OtpCode { get; set; } = null!;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
