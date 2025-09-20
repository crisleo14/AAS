using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Models.Reports;
using Accounting_System.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Accounting_System.Repository
{
    public class ReceiptRepo
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly GeneralRepo _generalRepo;

        public ReceiptRepo(ApplicationDbContext dbContext, GeneralRepo generalRepo)
        {
            _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
            _generalRepo = generalRepo;
        }

        public async Task<string> GenerateCRNo(CancellationToken cancellationToken = default)
        {
            var collectionReceipt = await _dbContext
                .CollectionReceipts
                .OrderByDescending(s => s.CollectionReceiptNo)
                .FirstOrDefaultAsync(cancellationToken);

            if (collectionReceipt != null)
            {
                string lastSeries = collectionReceipt.CollectionReceiptNo ?? throw new InvalidOperationException("CRNo is null pls Contact MIS Enterprise");
                string numericPart = lastSeries.Substring(2);
                int incrementedNumber = int.Parse(numericPart) + 1;

                return lastSeries.Substring(0,2) + incrementedNumber.ToString("D10");
            }
            else
            {
                return $"CR{1.ToString("D10")}";
            }
        }

        public async Task<List<CollectionReceipt>> GetCollectionReceiptsAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext
                .CollectionReceipts
                .Include(cr => cr.Customer)
                .Include(cr => cr.SalesInvoice)
                .ThenInclude(s => s!.Customer)
                .Include(cr => cr.SalesInvoice)
                .ThenInclude(s => s!.Product)
                .Include(cr => cr.ServiceInvoice)
                .ThenInclude(sv => sv!.Customer)
                .Include(cr => cr.ServiceInvoice)
                .ThenInclude(sv => sv!.Service)
                .ToListAsync(cancellationToken);
        }

        public async Task<CollectionReceipt> FindCR(int id, CancellationToken cancellationToken = default)
        {
            var collectionReceipt = await _dbContext
                .CollectionReceipts
                .Include(cr => cr.Customer)
                .Include(cr => cr.SalesInvoice)
                .ThenInclude(s => s!.Customer)
                .Include(cr => cr.SalesInvoice)
                .ThenInclude(s => s!.Product)
                .Include(cr => cr.ServiceInvoice)
                .ThenInclude(sv => sv!.Customer)
                .Include(cr => cr.ServiceInvoice)
                .ThenInclude(sv => sv!.Service)
                .FirstOrDefaultAsync(collectionReceipt => collectionReceipt.CollectionReceiptId == id, cancellationToken);

            if (collectionReceipt != null)
            {
                return collectionReceipt;
            }
            else
            {
                throw new ArgumentException("Invalid id value. The id must be greater than 0.");
            }
        }

        public async Task<int> UpdateInvoice(int id, decimal paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var si = await _dbContext
                .SalesInvoices
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == id, cancellationToken);

            if (si != null)
            {
                decimal netDiscount = si.Amount - si.Discount;

                var total = paidAmount + offsetAmount;
                si.AmountPaid += total;
                si.Balance = netDiscount - si.AmountPaid;

                if (si.Balance == 0 && si.AmountPaid == netDiscount)
                {
                    si.IsPaid = true;
                    si.Status = "Paid";
                }
                else if (si.AmountPaid > netDiscount)
                {
                    si.IsPaid = true;
                    si.Status = "OverPaid";
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("", "No record found");
            }
        }
        public async Task<int> UpdateMultipleInvoice(string[] siNo, decimal[] paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            if (siNo.IsNullOrEmpty())
            {
                var salesInvoice = new SalesInvoice();
                for (int i = 0; i < siNo.Length; i++)
                {
                    decimal netDiscount = salesInvoice.Amount - salesInvoice.Discount;

                    var siValue = siNo[i];
                    salesInvoice = await _dbContext.SalesInvoices
                                .FirstOrDefaultAsync(p => p.SalesInvoiceNo == siValue);

                    var amountPaid = salesInvoice!.AmountPaid + paidAmount[i] + offsetAmount;

                    if (!salesInvoice.IsPaid)
                    {
                        salesInvoice.AmountPaid += salesInvoice.Amount >= amountPaid ? paidAmount[i] + offsetAmount : paidAmount[i];

                        salesInvoice.Balance = netDiscount - salesInvoice.AmountPaid;

                        if (salesInvoice.Balance == 0 && salesInvoice.AmountPaid == netDiscount)
                        {
                            salesInvoice.IsPaid = true;
                            salesInvoice.Status = "Paid";
                        }
                        else if (salesInvoice.AmountPaid > netDiscount)
                        {
                            salesInvoice.IsPaid = true;
                            salesInvoice.Status = "OverPaid";
                        }
                    }
                    else
                    {
                        continue;
                    }
                    if (salesInvoice.Amount >= amountPaid)
                    {
                        offsetAmount = 0;
                    }
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("", "No record found");
            }
        }

        public async Task<int> RemoveSIPayment(int id, decimal paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var si = await _dbContext
                .SalesInvoices
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == id, cancellationToken);

            if (si != null)
            {
                decimal netDiscount = si.Amount - si.Discount;

                var total = paidAmount + offsetAmount;
                si.AmountPaid -= total;
                si.Balance -= netDiscount - total;

                if (si.IsPaid && si.Status == "Paid" || si.IsPaid && si.Status == "OverPaid")
                {
                    si.IsPaid = false;
                    si.Status = "Pending";
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("", "No record found");
            }
        }
        public async Task<int> RemoveSVPayment(int? id, decimal paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var sv = await _dbContext
                .ServiceInvoices
                .FirstOrDefaultAsync(si => si.ServiceInvoiceId == id, cancellationToken);

            if (sv != null)
            {
                var total = paidAmount + offsetAmount;
                sv.AmountPaid -= total;
                sv.Balance -= (sv.Total - sv.Discount) - total;

                if (sv.IsPaid && sv.Status == "Paid" || sv.IsPaid && sv.Status == "OverPaid")
                {
                    sv.IsPaid = false;
                    sv.Status = "Pending";
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("", "No record found");
            }
        }

        public async Task<int> UpdateSv(int id, decimal paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var sv = await _dbContext
                .ServiceInvoices
                .FirstOrDefaultAsync(si => si.ServiceInvoiceId == id, cancellationToken);

            if (sv != null)
            {
                var total = paidAmount + offsetAmount;
                sv.AmountPaid += total;
                sv.Balance = (sv.Total - sv.Discount) - sv.AmountPaid;

                if (sv.Balance == 0 && sv.AmountPaid == (sv.Total - sv.Discount))
                {
                    sv.IsPaid = true;
                    sv.Status = "Paid";
                }
                else if (sv.AmountPaid > (sv.Total - sv.Discount))
                {
                    sv.IsPaid = true;
                    sv.Status = "OverPaid";
                }

                return await _dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new ArgumentException("", "No record found");
            }
        }

        public async Task<List<Offsetting>> GetOffsettingAsync(string source, string reference, CancellationToken cancellationToken = default)
        {
            var result = await _dbContext
                .Offsettings
                .Where(o => o.Source == source && o.Reference == reference)
                .ToListAsync(cancellationToken);

            if (result.Any())
            {
                return result;
            }

            return new List<Offsetting>();
        }

        public async Task RemoveMultipleSIPayment(int[] id, decimal[] paidAmount, decimal offsetAmount, CancellationToken cancellationToken = default)
        {
            var salesInvoices = await _dbContext
                .SalesInvoices
                .Where(si => id.Contains(si.SalesInvoiceId))
                .ToListAsync(cancellationToken);

            if (salesInvoices.Any())
            {
                for (int i = 0; i < paidAmount.Length; i++)
                {
                    var total = paidAmount[i] + offsetAmount;
                    salesInvoices[i].AmountPaid -= total;
                    salesInvoices[i].Balance += total;

                    if (salesInvoices[i].IsPaid && salesInvoices[i].Status == "Paid" || salesInvoices[i].IsPaid && salesInvoices[i].Status == "OverPaid")
                    {
                        salesInvoices[i].IsPaid = false;
                        salesInvoices[i].Status = "Pending";
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        public async Task PostAsync(CollectionReceipt collectionReceipt, List<Offsetting> offsettings, CancellationToken cancellationToken = default)
        {
            var ledgers = new List<GeneralLedgerBook>();
            var accountTitlesDto = await _generalRepo.GetListOfAccountTitleDto(cancellationToken);
            var cashInBankTitle = accountTitlesDto.Find(c => c.AccountNumber == "101010100") ?? throw new ArgumentException("Account title '101010100' not found.");
            var arTradeTitle = accountTitlesDto.Find(c => c.AccountNumber == "101020100") ?? throw new ArgumentException("Account title '101020100' not found.");
            var arTradeCwt = accountTitlesDto.Find(c => c.AccountNumber == "101020200") ?? throw new ArgumentException("Account title '101020200' not found.");
            var arTradeCwv = accountTitlesDto.Find(c => c.AccountNumber == "101020300") ?? throw new ArgumentException("Account title '101020300' not found.");
            var cwt = accountTitlesDto.Find(c => c.AccountNumber == "101060400") ?? throw new ArgumentException("Account title '101060400' not found.");
            var cwv = accountTitlesDto.Find(c => c.AccountNumber == "101060600") ?? throw new ArgumentException("Account title '101060600' not found.");
            var offsetAmount = 0m;

            if (collectionReceipt.CashAmount > 0 || collectionReceipt.CheckAmount > 0 || collectionReceipt.ManagerCheckAmount > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = cashInBankTitle.AccountNumber,
                        AccountTitle = cashInBankTitle.AccountName,
                        Debit = collectionReceipt.CashAmount + collectionReceipt.CheckAmount + collectionReceipt.ManagerCheckAmount,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            if (collectionReceipt.EWT > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = cwt.AccountNumber,
                        AccountTitle = cwt.AccountName,
                        Debit = collectionReceipt.EWT,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            if (collectionReceipt.WVAT > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = cwv.AccountNumber,
                        AccountTitle = cwv.AccountName,
                        Debit = collectionReceipt.WVAT,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            foreach (var item in offsettings)
            {
                var account = accountTitlesDto.Find(c => c.AccountNumber == item.AccountNo) ??
                              throw new ArgumentException($"Account title '{item.AccountNo}' not found.");

                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = account.AccountNumber,
                        AccountTitle = account.AccountName,
                        Debit = item.Amount,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );

                offsetAmount += item.Amount;
            }

            if (collectionReceipt.CashAmount > 0 || collectionReceipt.CheckAmount > 0 || collectionReceipt.ManagerCheckAmount > 0 || offsetAmount > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = arTradeTitle.AccountNumber,
                        AccountTitle = arTradeTitle.AccountName,
                        Debit = 0,
                        Credit = collectionReceipt.CashAmount + collectionReceipt.CheckAmount + collectionReceipt.ManagerCheckAmount + offsetAmount,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate,
                    }
                );
            }

            if (collectionReceipt.EWT > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = arTradeCwt.AccountNumber,
                        AccountTitle = arTradeCwt.AccountName,
                        Debit = 0,
                        Credit = collectionReceipt.EWT,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            if (collectionReceipt.WVAT > 0)
            {
                ledgers.Add(
                    new GeneralLedgerBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        Reference = collectionReceipt.CollectionReceiptNo!,
                        Description = "Collection for Receivable",
                        AccountNo = arTradeCwv.AccountNumber,
                        AccountTitle = arTradeCwv.AccountName,
                        Debit = 0,
                        Credit = collectionReceipt.WVAT,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledgers, cancellationToken);

            #region Cash Receipt Book Recording

            var crb = new List<CashReceiptBook>();

            crb.Add(
                new CashReceiptBook
                {
                    Date = collectionReceipt.TransactionDate,
                    RefNo = collectionReceipt.CollectionReceiptNo!,
                    CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                    Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                    CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                    COA = $"{cashInBankTitle.AccountNumber} {cashInBankTitle.AccountName}",
                    Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                    Debit = collectionReceipt.CashAmount + collectionReceipt.CheckAmount + collectionReceipt.ManagerCheckAmount,
                    Credit = 0,
                    CreatedBy = collectionReceipt.CreatedBy,
                    CreatedDate = collectionReceipt.CreatedDate
                }

            );

            if (collectionReceipt.EWT > 0)
            {
                crb.Add(
                    new CashReceiptBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        RefNo = collectionReceipt.CollectionReceiptNo!,
                        CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                        Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                        CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                        COA = $"{cwt.AccountNumber} {cwt.AccountName}",
                        Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                        Debit = collectionReceipt.EWT,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            if (collectionReceipt.WVAT > 0)
            {
                crb.Add(
                    new CashReceiptBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        RefNo = collectionReceipt.CollectionReceiptNo!,
                        CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                        Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                        CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                        COA = $"{cwv.AccountNumber} {cwv.AccountName}",
                        Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                        Debit = collectionReceipt.WVAT,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            foreach (var item in offsettings)
            {
                var account = accountTitlesDto.Find(c => c.AccountNumber == item.AccountNo) ??
                              throw new ArgumentException($"Account title '{item.AccountNo}' not found.");

                crb.Add(
                    new CashReceiptBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        RefNo = collectionReceipt.CollectionReceiptNo!,
                        CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                        Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                        CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                        COA = $"{account.AccountNumber} {account.AccountName}",
                        Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                        Debit = item.Amount,
                        Credit = 0,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            crb.Add(
                new CashReceiptBook
                {
                    Date = collectionReceipt.TransactionDate,
                    RefNo = collectionReceipt.CollectionReceiptNo!,
                    CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                    Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                    CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                    COA = $"{arTradeTitle.AccountNumber} {arTradeTitle.AccountName}",
                    Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                    Debit = 0,
                    Credit = collectionReceipt.CashAmount + collectionReceipt.CheckAmount + collectionReceipt.ManagerCheckAmount + offsetAmount,
                    CreatedBy = collectionReceipt.CreatedBy,
                    CreatedDate = collectionReceipt.CreatedDate
                }
            );

            if (collectionReceipt.EWT > 0)
            {
                crb.Add(
                    new CashReceiptBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        RefNo = collectionReceipt.CollectionReceiptNo!,
                        CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                        Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                        CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                        COA = $"{arTradeCwt.AccountNumber} {arTradeCwt.AccountName}",
                        Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                        Debit = 0,
                        Credit = collectionReceipt.EWT,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            if (collectionReceipt.WVAT > 0)
            {
                crb.Add(
                    new CashReceiptBook
                    {
                        Date = collectionReceipt.TransactionDate,
                        RefNo = collectionReceipt.CollectionReceiptNo!,
                        CustomerName = collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.Customer!.CustomerName : collectionReceipt.MultipleSIId != null ? collectionReceipt.Customer!.CustomerName : collectionReceipt.ServiceInvoice!.Customer!.CustomerName,
                        Bank = collectionReceipt.CheckBank ?? (collectionReceipt.ManagerCheckBank ?? "--"),
                        CheckNo = collectionReceipt.CheckNo ?? (collectionReceipt.ManagerCheckNo ?? "--"),
                        COA = $"{arTradeCwv.AccountNumber} {arTradeCwv.AccountName}",
                        Particulars = (collectionReceipt.SalesInvoiceId != null ? collectionReceipt.SalesInvoice!.SalesInvoiceNo : collectionReceipt.MultipleSIId != null ? string.Join(", ", collectionReceipt.MultipleSI!.Select(si => si.ToString())) : collectionReceipt.ServiceInvoice!.ServiceInvoiceNo)!,
                        Debit = 0,
                        Credit = collectionReceipt.WVAT,
                        CreatedBy = collectionReceipt.CreatedBy,
                        CreatedDate = collectionReceipt.CreatedDate
                    }
                );
            }

            await _dbContext.AddRangeAsync(crb, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            #endregion

        }
    }
}
