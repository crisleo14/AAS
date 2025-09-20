using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class PurchaseOrderRepo
    {
        private readonly ApplicationDbContext _dbContext;

        public PurchaseOrderRepo(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<PurchaseOrder>> GetPurchaseOrderAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext
                .PurchaseOrders
                .Include(p => p.Supplier)
                .Include(p => p.Product)
                .ToListAsync(cancellationToken);
        }

        public async Task<string> GeneratePONo(CancellationToken cancellationToken = default)
        {
            var purchaseOrder = await _dbContext
                .PurchaseOrders
                .Where(po => !po.PurchaseOrderNo!.StartsWith("POBEG"))
                .OrderByDescending(s => s.PurchaseOrderNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (purchaseOrder != null)
            {
                string lastSeries = purchaseOrder.PurchaseOrderNo ?? throw new InvalidOperationException("PONo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"PO{1.ToString("D10")}";
            }
        }

        public async Task<int> GetSupplierNoAsync(int? id, CancellationToken cancellationToken = default)
        {
            if (id != 0)
            {
                var supplier = await _dbContext
                                .Suppliers
                                .FirstOrDefaultAsync(s => s.SupplierId == id, cancellationToken);
                return supplier!.Number;
            }
            else
            {
                throw new ArgumentException("No record found in supplier.");
            }
        }

        public async Task<string> GetProductNoAsync(int? id, CancellationToken cancellationToken = default)
        {
            if (id != 0)
            {
                var product = await _dbContext
                                .Products
                                .FirstOrDefaultAsync(s => s.ProductId == id, cancellationToken);
                return product!.ProductCode!;
            }

            throw new ArgumentException("No record found in supplier.");
        }

        public async Task<PurchaseOrder> FindPurchaseOrder(int? id, CancellationToken cancellationToken = default)
        {
            var po = await _dbContext
                .PurchaseOrders
                .Include(p => p.Supplier)
                .Include(p => p.Product)
                .FirstOrDefaultAsync(po => po.PurchaseOrderId == id, cancellationToken);

            if (po != null)
            {
                return po;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }
    }
}
