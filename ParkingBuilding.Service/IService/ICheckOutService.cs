using ParkingBuilding.Service.DTOs;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface ICheckOutService
    {
        Task<CheckoutResponse> CheckoutVehicleAsync(CheckoutRequest request, int currentStaffId);
    }
}
