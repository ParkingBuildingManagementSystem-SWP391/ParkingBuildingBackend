using ParkingBuilding.Repository.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.IRepository
{
    public interface ISessionRepository
    {
        Task<ParkingSession?> GetByIdAsync(long sessionId);
        Task UpdateAsync(ParkingSession session);
    }
}
