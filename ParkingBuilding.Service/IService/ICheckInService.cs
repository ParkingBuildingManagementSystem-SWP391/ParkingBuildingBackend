using ParkingBuilding.Service.DTOs;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface ICheckInService
    {
        Task<ScanCheckInResponse> CheckInVehicleAsync(CheckInRequest request);
        Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request);
        Task<ScanCheckInResponse> ScanQrCheckInAsync(string ticketCode, string? detectedPlate);

    }
}
