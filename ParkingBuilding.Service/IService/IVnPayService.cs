using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.IService
{
    public interface IVnPayService
    {
        string CreatePaymentUrl(string txnRef, decimal amount, string orderInfo, string returnUrl, string ipAddress);
    }
}
