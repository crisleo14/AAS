using Accounting_System.Data;
using Accounting_System.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class JournalVoucherRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public JournalVoucherRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<JournalVoucherHeader>> GetJournalVouchersAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.JournalVoucherHeaders
                .Include(j => j.CheckVoucherHeader)
                .ThenInclude(cv => cv!.Supplier)
                .ToListAsync(cancellationToken);
        }

        public async Task<string> GenerateJVNo(CancellationToken cancellationToken = default)
        {
            var journalVoucher = await _dbContext
                .JournalVoucherHeaders
                .OrderByDescending(j => j.JournalVoucherHeaderNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (journalVoucher != null)
            {
                string lastSeries = journalVoucher.JournalVoucherHeaderNo ?? throw new InvalidOperationException("JVNo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"JV{1.ToString("D10")}";
            }
        }
    }
}
