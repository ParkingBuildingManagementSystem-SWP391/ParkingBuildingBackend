using System;

namespace ParkingBuilding.Service.Service.Helpers
{
    public static class QrCodeParserHelper
    {
        public static bool TryParseQr(string? input, out string? ticketCode, out string? licensePlate, out int? sessionId, out string? slotName)
        {
            ticketCode = null;
            licensePlate = null;
            sessionId = null;
            slotName = null;

            if (string.IsNullOrWhiteSpace(input)) return false;

            if (!input.Contains("|") || !input.Contains("TICKET:")) return false;

            var parts = input.Split('|');
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length >= 2)
                {
                    string key = kv[0].Trim().ToUpper();
                    string val = string.Join(":", kv, 1, kv.Length - 1).Trim();

                    if (key == "TICKET")
                    {
                        ticketCode = val;
                    }
                    else if (key == "PLATE")
                    {
                        licensePlate = val;
                    }
                    else if (key == "SLOT")
                    {
                        slotName = val;
                    }
                    else if (key == "ID")
                    {
                        if (int.TryParse(val, out int id))
                        {
                            sessionId = id;
                        }
                    }
                }
            }

            return !string.IsNullOrEmpty(ticketCode);
        }
    }
}
