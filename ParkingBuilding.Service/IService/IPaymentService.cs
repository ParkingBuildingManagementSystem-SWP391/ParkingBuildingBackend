using ParkingBuilding.Service.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService { 
    public interface IPaymentService
    {
        Task<PaymentResultDto> ProcessCashPaymentAsync(CashPaymentDto request, int currentStaffId);
        Task<PaymentResultDto> CreateVnPayPaymentUrlAsync(CreateVnPayPaymentDto request, VnPayConfig config, int currentUserId);
        Task<PaymentResultDto> ConfirmVnPayPaymentAsync(string txnRef, decimal amount, string responseCode);


        Task<string?> GetPaymentStatusAsync(int invoiceId, int currentUserId, string currentUserRole);
        
    }
}   