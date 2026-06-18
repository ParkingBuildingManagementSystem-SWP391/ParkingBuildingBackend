using Microsoft.EntityFrameworkCore;
using ParkingBuilding.Repository.Entities;
using ParkingBuilding.Repository.IRepository;
using System;
using System.Threading.Tasks;

namespace ParkingBuilding.Repository.Repository
{
    public class InvoiceRepository : GenericRepository<Invoice>, IInvoiceRepository
    {
        public InvoiceRepository(ParkingManagementDbContext context) : base(context)
        {
        }

        /// <summary>
        /// Ghi đè phương thức AddAsync của lớp cha để thực hiện logic UPSERT (Thêm/Cập nhật hóa đơn chưa thanh toán).
        /// LƯU Ý: Đã lược bỏ lệnh SaveChangesAsync() để việc lưu DB được kiểm soát tập trung bởi UnitOfWork.
        /// </summary>
        public override async Task AddAsync(Invoice invoice)
        {
            var existingInvoice = await _context.Invoices.FirstOrDefaultAsync(i => i.SessionId == invoice.SessionId);

            if (existingInvoice != null)
            {
                if (existingInvoice.PaymentStatus != "SUCCESS")
                {
                    existingInvoice.TotalAmount = invoice.TotalAmount;
                    existingInvoice.PaymentMethod = invoice.PaymentMethod;
                    existingInvoice.PaymentStatus = invoice.PaymentStatus;
                    existingInvoice.UpdatedDate = DateTime.UtcNow;

                    if (string.IsNullOrEmpty(existingInvoice.TransactionCode))
                    {
                        existingInvoice.TransactionCode = invoice.TransactionCode;
                    }
                    else
                    {
                        invoice.TransactionCode = existingInvoice.TransactionCode;
                    }

                    _context.Invoices.Update(existingInvoice);
                    invoice.InvoiceId = existingInvoice.InvoiceId;
                }
            }
            else
            {
                await _dbSet.AddAsync(invoice);
            }
        }
    }
}