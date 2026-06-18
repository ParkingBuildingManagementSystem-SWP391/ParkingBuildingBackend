using ParkingBuilding.Service.DTOs;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IBookingService
    {
        Task<BookSlotResponse> BookSlotAsync(int userId, BookSlotRequest request);
    }
}
