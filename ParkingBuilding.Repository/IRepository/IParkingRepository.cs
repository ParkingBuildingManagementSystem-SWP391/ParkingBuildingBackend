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
        Task<bool> HasActiveReservationAsync(int userId); 
        Task CreateSessionAsync(ParkingSession session, ParkingSlot slot);

        Task<ParkingSession?> GetReservedSessionByLicenseAsync(string licenseVehicle);
        Task<ParkingSession?> GetReservedSessionByTicketIdAsync(int ticketId);

        Task UpdateSessionAndSlotAsync(ParkingSession session, ParkingSlot slot);

        Task<ParkingSlot?> GetAvailableSlotForWalkInAsync(int vehicleTypeId);
        Task<List<ParkingSession>> GetExpiredReservationsAsync();

        Task<ParkingSession?> GetReservedSessionByTicketCodeAsync(string ticketCode);

        Task<ParkingSession?> GetActiveSessionByTicketCodeAsync(string ticketCode);
        Task<ParkingSession?> GetActiveSessionByIdAsync(int sessionId);
        Task CompleteParkingSessionAsync(ParkingSession session, ParkingSlot slot, Invoice invoice);
        Task<User?> GetStaffByIdAsync(int staffId);
        Task<ParkingSession?> GetActiveSessionByLicensePlateAsync(string licensePlate);

        Task<List<ParkingSlot>> GetSlotsByFloorIdAsync(int floorId);
    }
}
