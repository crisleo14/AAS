using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Accounting_System.Repository
{
    public class SalesInvoiceRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public SalesInvoiceRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<SalesInvoice>> GetSalesInvoicesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext
                .SalesInvoices
                .Include(s => s.Product)
                .Include(c => c.Customer)
                .ToListAsync(cancellationToken);
        }

        public async Task<string> GenerateSINo(CancellationToken cancellationToken = default)
        {
            var salesInvoice = await _dbContext
                .SalesInvoices
                .OrderByDescending(s => s.SalesInvoiceNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (salesInvoice != null)
            {
                string lastSeries = salesInvoice.SalesInvoiceNo!;
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"SI{1.ToString("D10")}";
            }
        }

        public async Task<SalesInvoice> FindSalesInvoice(int? id, CancellationToken cancellationToken = default)
        {
            var invoice = await _dbContext
                .SalesInvoices
                .Include(c => c.Customer)
                .Include(s => s.Product)
                .FirstOrDefaultAsync(invoice => invoice.SalesInvoiceId == id);

            if (invoice != null)
            {
                return invoice;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }

        public DateOnly ComputeDueDateAsync(string customerTerms, DateOnly date, CancellationToken cancellationToken = default)
        {
            if (!customerTerms.IsNullOrEmpty())
            {
                DateOnly dueDate;

                switch (customerTerms)
                {
                    case "7D":
                        return date.AddDays(7);

                    case "10D":
                        return date.AddDays(7);

                    case "15D":
                        return date.AddDays(15);

                    case "30D":
                        return date.AddDays(30);

                    case "45D":
                    case "45PDC":
                        return date.AddDays(45);

                    case "60D":
                    case "60PDC":
                        return date.AddDays(60);

                    case "90D":
                        return date.AddDays(90);

                    case "M15":
                        return date.AddMonths(1).AddDays(15 - date.Day);

                    case "M30":
                        if (date.Month == 1)
                        {
                            dueDate = new DateOnly(date.Year, date.Month, 1).AddMonths(2).AddDays(-1);
                        }
                        else
                        {
                            dueDate = new DateOnly(date.Year, date.Month, 1).AddMonths(2).AddDays(-1);

                            if (dueDate.Day == 31)
                            {
                                dueDate = dueDate.AddDays(-1);
                            }
                        }
                        return dueDate;

                    case "M29":
                        if (date.Month == 1)
                        {
                            dueDate = new DateOnly(date.Year, date.Month, 1).AddMonths(2).AddDays(-1);
                        }
                        else
                        {
                            dueDate = new DateOnly(date.Year, date.Month, 1).AddMonths(2).AddDays(-1);

                            if (dueDate.Day == 31)
                            {
                                dueDate = dueDate.AddDays(-2);
                            }
                            else if (dueDate.Day == 30)
                            {
                                dueDate = dueDate.AddDays(-1);
                            }
                        }
                        return dueDate;

                    default:
                        return date;
                }
            }

            throw new ArgumentException("No record found.");
        }
    }
}
