using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Service.Service.Helpers
{
    public class VnPayLibrary
    {
        private readonly SortedDictionary<string, string> _requestData =
            new SortedDictionary<string, string>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, string> _responseData =
            new SortedDictionary<string, string>(StringComparer.Ordinal);

        public void AddRequestData(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _requestData.Add(key, value);
            }
        }

        public void AddResponseData(string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _responseData.Add(key, value);
            }
        }

        public string GetResponseData(string key)
        {
            return _responseData.TryGetValue(key, out var val) ? val : string.Empty;
        }

        public string CreateRequestUrl(string baseUrl, string hashSecret)
        {
            StringBuilder data = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in _requestData)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    data.Append(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value) + "&");
                }
            }

            string rawData = data.ToString();
            if (rawData.Length > 0)
            {
                rawData = rawData.Remove(rawData.Length - 1);
            }

            // Tạo mã băm HMAC-SHA512
            string vnp_SecureHash = ComputeHmacSha512(rawData, hashSecret);

            string paymentUrl = baseUrl + "?" + rawData + "&vnp_SecureHash=" + vnp_SecureHash;
            return paymentUrl;
        }

        public bool ValidateSignature(string inputHash, string secretKey)
        {
            StringBuilder data = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in _responseData)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    data.Append(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value) + "&");
                }
            }

            string rawData = data.ToString();
            if (rawData.Length > 0)
            {
                rawData = rawData.Remove(rawData.Length - 1);
            }

            string myChecksum = ComputeHmacSha512(rawData, secretKey);
            return myChecksum.Equals(inputHash, StringComparison.InvariantCultureIgnoreCase);
        }

        public string ComputeHmacSha512(string message, string secretKey)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(secretKey);
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            using (var hmac = new HMACSHA512(keyBytes))
            {
                byte[] hashBytes = hmac.ComputeHash(messageBytes);
                // Chuyển đổi sang chuỗi Hex in hoa (VNPay yêu cầu chữ ký in hoa)
                return BitConverter.ToString(hashBytes).Replace("-", "").ToUpper();
            }
        }
    }
}
