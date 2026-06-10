using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface ISlotRepository
    {
        Task<ParkingSlot?> GetByIdAsync(int slotId);
        Task UpdateAsync(ParkingSlot slot);
        Task<ParkingSlot?> GetByNameAsync(string name);
    }
}
