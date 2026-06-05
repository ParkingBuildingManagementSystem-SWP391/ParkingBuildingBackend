using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class UpdateUserRequestDto
    {
        [Required(ErrorMessage = "UserId là bắt buộc.")]
        public int UserId { get; set; }

        [DefaultValue("")]
        public string RoleName { get; set; } = null!;
        [DefaultValue("")]
        public string userName { get; set; } = null!;
        [DefaultValue("")]
        public string email { get; set; } = null!;
        [DefaultValue("")]
        public string phoneNumber { get; set; } = null!;
        
    }
}
