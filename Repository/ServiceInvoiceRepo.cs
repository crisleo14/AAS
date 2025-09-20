using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class ServiceInvoiceRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public ServiceInvoiceRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<ServiceInvoice>> GetServiceInvoicesAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext
                .ServiceInvoices
                .Include(sv => sv.Customer)
                .Include(sv => sv.Service)
                .ToListAsync(cancellationToken);
        }

        public async Task<string> GenerateSvNo(CancellationToken cancellationToken = default)
        {
            var serviceInvoice = await _dbContext
                .ServiceInvoices
                .OrderByDescending(s => s.ServiceInvoiceNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (serviceInvoice != null)
            {
                string lastSeries = serviceInvoice.ServiceInvoiceNo!;
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"SV{1.ToString("D10")}";
            }
        }

        public async Task<ServiceInvoice> FindSv(int id, CancellationToken cancellationToken = default)
        {
            var serviceInvoice = await _dbContext
                .ServiceInvoices
                .Include(s => s.Customer)
                .Include(s => s.Service)
                .FirstOrDefaultAsync(s => s.ServiceInvoiceId == id, cancellationToken);

            if (serviceInvoice != null)
            {
                return serviceInvoice;
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

        public async Task<Customer> FindCustomerAsync(int? id, CancellationToken cancellationToken = default)
        {
            var customer = await _dbContext
                .Customers
                .FirstOrDefaultAsync(s => s.CustomerId == id, cancellationToken);

            if (customer != null)
            {
                return customer;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }
    }
}
