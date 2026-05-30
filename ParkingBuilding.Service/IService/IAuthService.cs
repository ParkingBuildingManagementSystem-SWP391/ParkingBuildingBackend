
using ParkingBuilding.Service.DTOs;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{

    public interface IAuthService
    {
        Task<string> RegisterAsync(RegisterRequest request);
        Task<AuthResponse> VerifyOtpAsync(VerifyOtpRequest request);
        Task<AuthResponse> LoginAsync(LoginRequest request);
        Task<AuthResponse> ContinueWithGoogleAsync(GoogleLoginRequest request);
    }
}
