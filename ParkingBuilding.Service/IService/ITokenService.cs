
using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{

    public interface ITokenService
    {
        string GenerateJwtToken(User user);
    }
}
