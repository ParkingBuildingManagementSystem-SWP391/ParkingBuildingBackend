using System;

namespace ParkingBuilding.Service.DTOs
{
    public class ReportExportRequest
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Format { get; set; } = "EXCEL"; // "EXCEL" hoặc "PDF"
        public int? VehicleTypeId { get; set; }
    }

    public class ReportExportResponse
    {
        public byte[] FileBytes { get; set; } = Array.Empty<byte>();
        public string ContentType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}
