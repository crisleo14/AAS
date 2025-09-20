using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class CheckVoucherRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public CheckVoucherRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<CheckVoucherHeader>> GetCheckVouchersAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.CheckVoucherHeaders
                .Include(s => s.Supplier)
                .ToListAsync(cancellationToken);
        }

        public async Task<string> GenerateCVNo(CancellationToken cancellationToken = default)
        {
            var checkVoucher = await _dbContext
                .CheckVoucherHeaders
                .OrderByDescending(s => s.CheckVoucherHeaderNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (checkVoucher != null)
            {
                string lastSeries = checkVoucher.CheckVoucherHeaderNo ?? throw new InvalidOperationException("CVNo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0, 2) + incrementedNumber.ToString("D10");
            }

            return $"CV{1.ToString("D10")}";
        }

        public async Task UpdateInvoicingVoucher(decimal paymentAmount, int invoiceVoucherId, CancellationToken cancellationToken = default)
        {
            var invoiceVoucher = await _dbContext.CheckVoucherHeaders
                .FirstOrDefaultAsync(x => x.CheckVoucherHeaderId == invoiceVoucherId, cancellationToken);

            if (invoiceVoucher != null)
            {
                invoiceVoucher.AmountPaid += paymentAmount;

                if (invoiceVoucher.AmountPaid >= invoiceVoucher.Total)
                {
                    invoiceVoucher.IsPaid = true;
                }
            }
            else
            {
                throw new InvalidOperationException($"Check voucher with id '{invoiceVoucherId}' not found.");
            }
        }
    }
}
