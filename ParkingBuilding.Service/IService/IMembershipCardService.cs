using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IMembershipCardService
    {
        Task<MembershipCardRegistrationResponseDto> RegisterMembershipCardAsync(int userId, RegisterMembershipCardDto dto, string ipAddress);
        Task<PaymentResultDto> ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus);
        Task<List<object>> GetMyActiveCardsAsync(int userId);
        Task<List<object>> GetActiveTiersAsync();
        Task<bool> CancelMembershipCardAsync(int cardId, int userId);
        Task<bool> UpdateMembershipVehiclesAsync(int cardId, int userId, List<string> newPlates);
    }
}
