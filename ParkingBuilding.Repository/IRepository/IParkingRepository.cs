using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface IParkingRepository
    {
        Task<ParkingSlot?> GetSlotByIdAsync(int slotId);
        Task<bool> HasActiveReservationAsync(int userId); // Hàm kiểm tra spam
        Task CreateSessionAsync(ParkingSession session, ParkingSlot slot);

    }
}
