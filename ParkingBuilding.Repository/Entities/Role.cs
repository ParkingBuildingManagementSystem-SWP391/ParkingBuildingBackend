using System;
using System.Collections.Generic;

namespace ParkingBuilding.Repository.Entities;

public partial class Role
{
    public int RoleId { get; set; }

    public string RoleName { get; set; } = null!;

    public bool IsDeleted { get; set; }

    public virtual ICollection<User> Users { get; set; } = new List<User>();
    public string PhoneNumber { get; set; }
}
