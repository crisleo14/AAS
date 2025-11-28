using Accounting_System.Data;
using Accounting_System.Models.Reports;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using Accounting_System.Models;
using Accounting_System.Models.DTOs;
using Microsoft.AspNetCore.Identity;

namespace Accounting_System.Repository
{
    public class GeneralRepo
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private const decimal VatRate = 0.12m;

        public GeneralRepo(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager)
        {
            _dbContext = dbContext;
            _userManager = userManager;
        }

        public async Task<int> RemoveRecords<TEntity>(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
       where TEntity : class
        {
            var entitySet = _dbContext.Set<TEntity>();
            var entitiesToRemove = await entitySet.Where(predicate).ToListAsync(cancellationToken);

            if (entitiesToRemove.Any())
            {
                foreach (var entity in entitiesToRemove)
                {
                    entitySet.Remove(entity);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                return entitiesToRemove.Count;
            }

            throw new ArgumentException($"No entities found with identifier value: '{predicate.Body}'");
        }

        public bool IsJournalEntriesBalanced(IEnumerable<GeneralLedgerBook> ledgers)
        {
            try
            {
                var generalLedgerBooks = ledgers.ToList();
                var totalDebit = Math.Round(generalLedgerBooks.Sum(j => j.Debit), 2);
                var totalCredit = Math.Round(generalLedgerBooks.Sum(j => j.Credit), 2);

                return totalDebit == totalCredit;
            }
            catch (Exception ex)
            {
                throw new ArgumentException(ex.Message);
            }
        }

        public async Task<List<SelectListItem>> GetSupplierListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Suppliers
                .Select(supp => new SelectListItem
                {
                    Value = supp.SupplierId.ToString(),
                    Text = supp.SupplierName
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetPurchaseOrderListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.PurchaseOrders
                .Select(po => new SelectListItem
                {
                    Value = po.PurchaseOrderNo!.ToString(),
                    Text = po.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetReceivingReportListAsync(string[] rrNos, CancellationToken cancellationToken = default)
        {
            var rrNoHashSet = new HashSet<string>(rrNos);

            var rrList = await _dbContext.ReceivingReports
                .OrderBy(rr =>
                    rrNoHashSet.Contains(rr.ReceivingReportNo!)
                        // ReSharper disable once EntityFramework.UnsupportedServerSideFunctionCall
                        ? Array.IndexOf(rrNos, rr.ReceivingReportNo)
                        : int.MaxValue) // Order by index in rrNos array if present in HashSet
                .ThenBy(rr => rr.ReceivingReportId) // Secondary ordering by Id
                .Select(rr => new SelectListItem
                {
                    Value = rr.ReceivingReportNo!.ToString(),
                    Text = rr.ReceivingReportNo
                })
                .ToListAsync(cancellationToken);

            return rrList;
        }

        public async Task<List<SelectListItem>> GetBankAccountListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.BankAccounts
                .Select(ba => new SelectListItem
                {
                    Value = ba.BankAccountId.ToString(),
                    Text = ba.AccountName
                })
                .ToListAsync(cancellationToken);
        }

        public async Task<List<SelectListItem>> GetCOAListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.ChartOfAccounts
                .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber!.Contains(excludedNumber)) && !coa.HasChildren)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);
        }

        public decimal ComputeNetOfVat(decimal grossAmount)
        {
            if (grossAmount <= 0)
            {
                throw new ArgumentException("Gross amount cannot be negative or zero.");
            }

            return grossAmount / (1 + VatRate);
        }

        public decimal ComputeVatAmount(decimal netOfVatAmount)
        {
            if (netOfVatAmount <= 0)
            {
                throw new ArgumentException("Net of vat amount cannot be negative or zero.");
            }

            return netOfVatAmount * VatRate;
        }

        public decimal ComputeEwtAmount(decimal netOfVatAmount, decimal percent)
        {
            if (netOfVatAmount <= 0)
            {
                throw new ArgumentException("Net of vat amount cannot be negative or zero.");
            }

            if (percent <= 0)
            {
                throw new ArgumentException("Ewt percent cannot be negative or zero.");
            }

            return netOfVatAmount * percent;
        }

        public decimal ComputeNetOfEwt(decimal grossAmount, decimal ewtAmount)
        {
            if (grossAmount <= 0)
            {
                throw new ArgumentException("Gross amount cannot be negative or zero.");
            }

            if (ewtAmount <= 0)
            {
                throw new ArgumentException("Ewt amount cannot be negative or zero.");
            }

            return grossAmount - ewtAmount;
        }

        public async Task<List<AccountTitleDto>> GetListOfAccountTitleDto(CancellationToken cancellationToken = default)
        {
            return await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .Select(coa => new AccountTitleDto
                {
                    AccountId = coa.AccountId,
                    AccountNumber = coa.AccountNumber!,
                    AccountName = coa.AccountName
                })
                .ToListAsync(cancellationToken);
        }

        public (string AccountNo, string AccountTitle) GetInventoryAccountTitle(string productCode)
        {
            return productCode switch
            {
                "PET001" => ("101040100", "Inventory - Biodiesel"),
                "PET002" => ("101040200", "Inventory - Econogas"),
                "PET003" => ("101040300", "Inventory - Envirogas"),
                _ => throw new ArgumentException($"Invalid product code: {productCode}"),
            };
        }

        public (string AccountNo, string AccountTitle) GetSalesAccountTitle(string productCode)
        {
            return productCode switch
            {
                "PET001" => ("401010100", "Sales - Biodiesel"),
                "PET002" => ("401010200", "Sales - Econogas"),
                "PET003" => ("401010300", "Sales - Envirogas"),
                _ => throw new ArgumentException($"Invalid product code: {productCode}"),
            };
        }

        public async Task<string> GetUserFullNameAsync(string userName)
        {
            try
            {
                var user = await _userManager.FindByNameAsync(userName) as ApplicationUser;

                return $"{user?.FirstName.ToUpper()} {user?.LastName.ToUpper()}";
            }
            catch (Exception ex)
            {
                throw new ArgumentException(ex.Message);
            }
        }
    }
}
