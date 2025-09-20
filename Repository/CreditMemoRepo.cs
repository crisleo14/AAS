using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class CreditMemoRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public CreditMemoRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        }

        public async Task<string> GenerateCMNo(CancellationToken cancellationToken = default)
        {
            var creditMemo = await _dbContext
                .CreditMemos
                .OrderByDescending(s => s.CreditMemoNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (creditMemo != null)
            {
                string lastSeries = creditMemo.CreditMemoNo ?? throw new InvalidOperationException("CMNo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementNumber.ToString("D10");
            }
            else
            {
                return $"CM{1.ToString("D10")}";
            }
        }

        public async Task<string> GetSINoAsync(int? id, CancellationToken cancellationToken = default)
        {
            if (id != 0)
            {
                var si = await _dbContext
                                .SalesInvoices
                                .FirstOrDefaultAsync(po => po.SalesInvoiceId == id, cancellationToken);
                return si!.SalesInvoiceNo!;
            }

            throw new ArgumentException("No record found in supplier.");
        }

        public async Task<string> GetSVNoAsync(int? id, CancellationToken cancellationToken = default)
        {
            if (id != 0)
            {
                var sv = await _dbContext
                                .ServiceInvoices
                                .FirstOrDefaultAsync(po => po.ServiceInvoiceId == id, cancellationToken);
                return sv!.ServiceInvoiceNo!;
            }

            throw new ArgumentException("No record found in supplier.");
        }

        public async Task<CreditMemo> FindCM(int id, CancellationToken cancellationToken = default)
        {
            var creditMemo = await _dbContext
                .CreditMemos
                .Include(c => c.SalesInvoice)
                .ThenInclude(s => s!.Product)
                .Include(c => c.SalesInvoice)
                .ThenInclude(s => s!.Customer)
                .Include(c => c.ServiceInvoice)
                .ThenInclude(sv => sv!.Customer)
                .Include(c => c.ServiceInvoice)
                .ThenInclude(sv => sv!.Service)
                .FirstOrDefaultAsync(creditMemo => creditMemo.CreditMemoId == id, cancellationToken);

            if (creditMemo != null)
            {
                return creditMemo;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }

        public async Task<Services> GetServicesAsync(int? id, CancellationToken cancellationToken = default)
        {
            var services = await _dbContext
                .Services
                .FirstOrDefaultAsync(s => s.ServiceId == id, cancellationToken);

            if (services != null)
            {
                return services;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }
        public async Task<List<CreditMemo>> GetCreditMemosAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.CreditMemos
                .Include(cm => cm.SalesInvoice)
                .ThenInclude(s => s!.Customer)
                .Include(cm => cm.SalesInvoice)
                .ThenInclude(s => s!.Product)
                .Include(cm => cm.ServiceInvoice)
                .ThenInclude(sv => sv!.Customer)
                .Include(cm => cm.ServiceInvoice)
                .ThenInclude(sv => sv!.Service)
                .ToListAsync(cancellationToken);
        }
    }
}
