using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface ISlotRepository : IGenericRepository<ParkingSlot>
    {
        Task<ParkingSlot?> GetByNameAsync(string name);
    }
}
