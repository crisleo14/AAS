using System.Security.Claims;
using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Accounting_System.Models.Reports;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;

namespace Accounting_System.Repository
{
    public class ReceivingReportRepo
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly GeneralRepo _generalRepo;

        private readonly InventoryRepo _inventoryRepo;

        public ReceivingReportRepo(ApplicationDbContext dbContext, GeneralRepo generalRepo, InventoryRepo inventoryRepo)
        {
            _dbContext = dbContext;
            _generalRepo = generalRepo;
            _inventoryRepo = inventoryRepo;
        }

        public async Task<string> GenerateRRNo(CancellationToken cancellationToken = default)
        {
            var receivingReport = await _dbContext
                .ReceivingReports
                .Where(rr => !rr.ReceivingReportNo!.StartsWith("RRBEG"))
                .OrderByDescending(s => s.ReceivingReportNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (receivingReport != null)
            {
                string lastSeries = receivingReport.ReceivingReportNo ?? throw new InvalidOperationException("RRNo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"RR{1.ToString("D10")}";
            }
        }

        public async Task<string> GetPONoAsync(int? id, CancellationToken cancellationToken = default)
        {
            if (id != 0)
            {
                var po = await _dbContext
                                .PurchaseOrders
                                .FirstOrDefaultAsync(po => po.PurchaseOrderId == id, cancellationToken);
                return po!.PurchaseOrderNo!;
            }
            else
            {
                throw new ArgumentException("No record found in supplier.");
            }
        }

        public async Task UpdatePOAsync(int id, decimal quantityReceived, CancellationToken cancellationToken = default)
        {
            var po = await _dbContext.PurchaseOrders
                    .FirstOrDefaultAsync(po => po.PurchaseOrderId == id, cancellationToken);

            if (po != null)
            {
                po.QuantityReceived += quantityReceived;

                if (po.QuantityReceived == po.Quantity)
                {
                    po.IsReceived = true;
                    po.ReceivedDate = DateTime.Now;

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                if (po.QuantityReceived > po.Quantity)
                {
                    throw new ArgumentException("Input is exceed to remaining quantity received");
                }
            }
            else
            {
                throw new ArgumentException("No record found.");
            }
        }

        public async Task<int> RemoveQuantityReceived(int? id, decimal quantityReceived, CancellationToken cancellationToken = default)
        {
            var po = await _dbContext.PurchaseOrders
                    .FirstOrDefaultAsync(po => po.PurchaseOrderId == id, cancellationToken);

            if (po != null)
            {
                po.QuantityReceived -= quantityReceived;

                if (po.IsReceived)
                {
                    po.IsReceived = false;
                    po.ReceivedDate = DateTime.MaxValue;
                }
                if (po.QuantityReceived > po.Quantity)
                {
                    throw new ArgumentException("Input is exceed to remaining quantity received");
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("No record found.");
            }
        }

        public async Task<DateOnly> ComputeDueDateAsync(int? poId, DateOnly rrDate, CancellationToken cancellationToken = default)
        {
            var po = await _dbContext
                .PurchaseOrders
                .FirstOrDefaultAsync(po => po.PurchaseOrderId == poId, cancellationToken);

            if (po != null)
            {
                DateOnly dueDate;

                switch (po.Terms)
                {
                    case "7D":
                        return rrDate.AddDays(7);

                    case "10D":
                        return rrDate.AddDays(7);

                    case "15D":
                        return rrDate.AddDays(15);

                    case "30D":
                        return rrDate.AddDays(30);

                    case "45D":
                    case "45PDC":
                        return rrDate.AddDays(45);

                    case "60D":
                    case "60PDC":
                        return rrDate.AddDays(60);

                    case "90D":
                        return rrDate.AddDays(90);

                    case "M15":
                        return rrDate.AddMonths(1).AddDays(15 - rrDate.Day);

                    case "M30":
                        if (rrDate.Month == 1)
                        {
                            dueDate = new DateOnly(rrDate.Year, rrDate.Month, 1).AddMonths(2).AddDays(-1);
                        }
                        else
                        {
                            dueDate = new DateOnly(rrDate.Year, rrDate.Month, 1).AddMonths(2).AddDays(-1);

                            if (dueDate.Day == 31)
                            {
                                dueDate = dueDate.AddDays(-1);
                            }
                        }
                        return dueDate;

                    case "M29":
                        if (rrDate.Month == 1)
                        {
                            dueDate = new DateOnly(rrDate.Year, rrDate.Month, 1).AddMonths(2).AddDays(-1);
                        }
                        else
                        {
                            dueDate = new DateOnly(rrDate.Year, rrDate.Month, 1).AddMonths(2).AddDays(-1);

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
                        return rrDate;
                }
            }

            throw new ArgumentException("No record found.");
        }

        public async Task<ReceivingReport> FindRR(int id, CancellationToken cancellationToken = default)
        {
            var rr = await _dbContext
                .ReceivingReports
                .Include(rr => rr.PurchaseOrder)
                .ThenInclude(po => po!.Product)
                .Include(rr => rr.PurchaseOrder)
                .ThenInclude(po => po!.Supplier)
                .FirstOrDefaultAsync(rr => rr.ReceivingReportId == id, cancellationToken);

            if (rr != null)
            {
                return rr;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }

        public async Task<PurchaseOrder> GetPurchaseOrderAsync(int id, CancellationToken cancellationToken = default)
        {
            var po = await _dbContext
                .PurchaseOrders
                .Include(po => po.Product)
                .Include(po => po.Supplier)
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

        public async Task<List<ReceivingReport>> GetReceivingReportsAsync(CancellationToken cancellationToken = default)
        {
            var rr = await _dbContext.ReceivingReports
                .Include(p => p.PurchaseOrder)
                .ThenInclude(s => s!.Supplier)
                .Include(p => p.PurchaseOrder)
                .ThenInclude(prod => prod!.Product)
                .ToListAsync(cancellationToken);

            if (rr.Any() && rr.Count > 0)
            {
                return rr;
            }

            return new List<ReceivingReport>();
        }

        public async Task PostAsync(ReceivingReport model, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            #region --General Ledger Recording

            var ledgers = new List<GeneralLedgerBook>();

            decimal netOfVatAmount;
            decimal vatAmount = 0;
            decimal ewtAmount = 0;
            decimal netOfEwtAmount;

            if (model.PurchaseOrder!.Supplier!.VatType == CS.VatType_Vatable)
            {
                netOfVatAmount = _generalRepo.ComputeNetOfVat(model.Amount);
                vatAmount = _generalRepo.ComputeVatAmount(netOfVatAmount);
            }
            else
            {
                netOfVatAmount = model.Amount;
            }

            if (model.PurchaseOrder.Supplier.TaxType == CS.TaxType_WithTax)
            {
                ewtAmount = _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m);
                netOfEwtAmount = _generalRepo.ComputeNetOfEwt(model.Amount, ewtAmount);
            }
            else
            {
                netOfEwtAmount = model.Amount;
            }

            var (inventoryAcctNo, _) = _generalRepo.GetInventoryAccountTitle(model.PurchaseOrder.Product!.ProductCode!);
            var accountTitlesDto = await _generalRepo.GetListOfAccountTitleDto(cancellationToken);
            var vatInputTitle = accountTitlesDto.Find(c => c.AccountNumber == "101060200") ?? throw new ArgumentException("Account title '101060200' not found.");
            var ewtTitle = accountTitlesDto.Find(c => c.AccountNumber == "201030210") ?? throw new ArgumentException("Account title '201030200' not found.");
            var apTradeTitle = accountTitlesDto.Find(c => c.AccountNumber == "202010100") ?? throw new ArgumentException("Account title '202010100' not found.");
            var inventoryTitle = accountTitlesDto.Find(c => c.AccountNumber == inventoryAcctNo) ?? throw new ArgumentException($"Account title '{inventoryAcctNo}' not found.");

            ledgers.Add(new GeneralLedgerBook
            {
                Date = model.Date,
                Reference = model.ReceivingReportNo!,
                Description = "Receipt of Goods",
                AccountNo = inventoryTitle.AccountNumber,
                AccountTitle = inventoryTitle.AccountName,
                Debit = netOfVatAmount,
                Credit = 0,
                CreatedBy = model.CreatedBy,
                CreatedDate = model.CreatedDate,
            });

            if (vatAmount > 0)
            {
                ledgers.Add(new GeneralLedgerBook
                {
                    Date = model.Date,
                    Reference = model.ReceivingReportNo!,
                    Description = "Receipt of Goods",
                    AccountNo = vatInputTitle.AccountNumber,
                    AccountTitle = vatInputTitle.AccountName,
                    Debit = vatAmount,
                    Credit = 0,
                    CreatedBy = model.CreatedBy,
                    CreatedDate = model.CreatedDate,
                });
            }

            ledgers.Add(new GeneralLedgerBook
            {
                Date = model.Date,
                Reference = model.ReceivingReportNo!,
                Description = "Receipt of Goods",
                AccountNo = apTradeTitle.AccountNumber,
                AccountTitle = apTradeTitle.AccountName,
                Debit = 0,
                Credit = netOfEwtAmount,
                CreatedBy = model.CreatedBy,
                CreatedDate = model.CreatedDate,
            });

            if (ewtAmount > 0)
            {
                ledgers.Add(new GeneralLedgerBook
                {
                    Date = model.Date,
                    Reference = model.ReceivingReportNo!,
                    Description = "Receipt of Goods",
                    AccountNo = ewtTitle.AccountNumber,
                    AccountTitle = ewtTitle.AccountName,
                    Debit = 0,
                    Credit = ewtAmount,
                    CreatedBy = model.CreatedBy,
                    CreatedDate = model.CreatedDate,
                });
            }

            if (!_generalRepo.IsJournalEntriesBalanced(ledgers))
            {
                throw new ArgumentException("Debit and Credit is not equal, check your entries.");
            }

            await _dbContext.AddRangeAsync(ledgers, cancellationToken);

            #endregion --General Ledger Recording

            #region--Inventory Recording

            await _inventoryRepo.AddPurchaseToInventoryAsync(model, user, cancellationToken);

            #endregion

            await UpdatePOAsync(model.PurchaseOrder.PurchaseOrderId, model.QuantityReceived, cancellationToken);

            #region --Purchase Book Recording

            PurchaseJournalBook purchaseBook = new()
            {
                Date = model.Date,
                SupplierName = model.PurchaseOrder.Supplier.SupplierName,
                SupplierTin = model.PurchaseOrder.Supplier.SupplierTin,
                SupplierAddress = model.PurchaseOrder.Supplier.SupplierAddress,
                DocumentNo = model.ReceivingReportNo!,
                Description = model.PurchaseOrder.Product.ProductName,
                Amount = model.Amount,
                VatAmount = vatAmount,
                WhtAmount = ewtAmount,
                NetPurchases = netOfVatAmount,
                CreatedBy = model.CreatedBy,
                PONo = model.PurchaseOrder.PurchaseOrderNo!,
                DueDate = model.DueDate,
            };

            await _dbContext.AddAsync(purchaseBook, cancellationToken);
            #endregion --Purchase Book Recording

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
