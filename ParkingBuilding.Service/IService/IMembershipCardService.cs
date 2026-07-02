using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IMembershipCardService
    {
        Task<MembershipCardRegistrationResponseDto> RegisterMembershipCardAsync(int userId, RegisterMembershipCardDto dto, string ipAddress);
        Task<PaymentResultDto> ConfirmMembershipCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus);
        Task<object?> GetMyActiveCardAsync(int userId);
        Task<List<object>> GetActiveTiersAsync();
    }
}
