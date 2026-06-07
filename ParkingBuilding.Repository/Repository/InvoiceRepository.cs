using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class InvoiceRepository : IInvoiceRepository
    {
        private readonly ParkingManagementDbContext _context;

        public InvoiceRepository(ParkingManagementDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(Invoice invoice)
        {
            var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.SessionId == invoice.SessionId);

            if (existingInvoice != null)
            {
                // Nếu hóa đơn đã tồn tại và chưa thanh toán thành công, thực hiện cập nhật (UPSERT)
                if (existingInvoice.PaymentStatus != "SUCCESS")
                {
                    existingInvoice.TotalAmount = invoice.TotalAmount;
                    existingInvoice.PaymentMethod = invoice.PaymentMethod;
                    existingInvoice.PaymentStatus = invoice.PaymentStatus;
                    existingInvoice.UpdatedDate = DateTime.UtcNow;

                    // Chỉ gán mã giao dịch mới nếu mã cũ đang trống hoặc null
                    if (string.IsNullOrEmpty(existingInvoice.TransactionCode))
                    {
                        existingInvoice.TransactionCode = invoice.TransactionCode;
                    }
                    else
                    {
                        // Đồng bộ lại mã cũ ngược lại cho biến đang chạy trong bộ nhớ để tạo URL VNPay đúng
                        invoice.TransactionCode = existingInvoice.TransactionCode;
                    }

                    _context.Invoices.Update(existingInvoice);
                    await _context.SaveChangesAsync();
                    invoice.InvoiceId = existingInvoice.InvoiceId;
                }
            }
            else
            {
                // Nếu chưa có hóa đơn, tiến hành thêm mới bình thường
                await _context.Invoices.AddAsync(invoice);
                await _context.SaveChangesAsync();
            }
        }


        public async Task<Invoice?> GetByIdAsync(int invoiceId)
        {
            return await _context.Invoices
                .Include(i => i.Session) // <-- Nạp kèm thông tin phiên đỗ
                .FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
        }

    }
}
