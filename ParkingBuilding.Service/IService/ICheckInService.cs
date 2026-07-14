using ParkingBuilding.Service.DTOs;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface ICheckInService
    {
        Task<ScanCheckInResponse> CheckInVehicleAsync(CheckInRequest request, int currentStaffId);
        Task<WalkInResponse> WalkInCheckInAsync(WalkInRequest request, int currentStaffId);
        Task<ScanCheckInResponse> ScanQrCheckInAsync(string ticketCode, string? detectedPlate, int currentStaffId);

    }
}
