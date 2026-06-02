using ParkingBuilding.Service.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IParkingService
    {
        Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request);
        Task<bool> CheckInVehicleAsync(CheckInRequest request);

        Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request);


    }
}
