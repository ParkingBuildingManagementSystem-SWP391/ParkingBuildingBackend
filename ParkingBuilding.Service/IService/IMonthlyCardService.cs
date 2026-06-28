using ParkingBuilding.Service.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IMonthlyCardService
    {
        // 1. Tạo yêu cầu đăng ký thẻ tháng và trả về đường dẫn VNPay
        Task<MonthlyCardRegistrationResponseDto> RegisterMonthlyCardAsync(int userId, RegisterMonthlyCardDto dto, string ipAddress);

        // 2. Xác nhận thanh toán từ VNPay IPN và lưu/kích hoạt thẻ tháng trong DB
        Task<PaymentResultDto> ConfirmMonthlyCardPaymentAsync(string txnRef, decimal amount, string responseCode, string transactionStatus);
    }
}
