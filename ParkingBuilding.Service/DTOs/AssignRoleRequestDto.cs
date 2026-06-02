using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.DTOs
{
    public class AssignRoleRequestDto
    {
        [Required(ErrorMessage = "UserId là bắt buộc.")]
        public int UserId { get; set; }

        [Required(ErrorMessage = "Tên role mong muốn là bắt buộc.")]
        public string RoleName { get; set; } = null!;
    }
}
