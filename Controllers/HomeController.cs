using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using Accounting_System.Repository;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ApplicationDbContext _dbContext;
        private readonly GeneralRepo _generalRepo;

        public HomeController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, GeneralRepo generalRepo)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
            ViewData["Id"] = createdBy;

            #region -- Query to count how many in each document to show in graph --

            var salesInvoiceSummary = await _dbContext.SalesInvoices
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var serviceInvoiceSummary = await _dbContext.ServiceInvoices
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var collectionReceiptSummary = await _dbContext.CollectionReceipts
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var debitMemoSummary = await _dbContext.DebitMemos
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var creditMemoSummary = await _dbContext.CreditMemos
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var purchaseOrderSummary = await _dbContext.PurchaseOrders
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var receivingReportSummary = await _dbContext.ReceivingReports
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var checkVoucherSummary = await _dbContext.CheckVoucherHeaders
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            var journalVoucherSummary = await _dbContext.JournalVoucherHeaders
                .GroupBy(i => 1)
                .Select(g => new List<int>
                {
                g.Count(i => i.IsPosted),
                g.Count(i => i.IsCanceled),
                g.Count(i => i.IsVoided)
                })
                .FirstOrDefaultAsync(cancellationToken) ?? [0, 0, 0];

            #endregion -- Query to count how many in each document to show in graph --

            #region -- Query of length to change the range of graph --

            var maxCountSalesInvoice = salesInvoiceSummary.Max();
            var maxCountServiceInvoice = serviceInvoiceSummary.Max();
            var maxCountCr = collectionReceiptSummary.Max();
            var maxCountDm = debitMemoSummary.Max();
            var maxCountCm = creditMemoSummary.Max();
            var maxCountPo = purchaseOrderSummary.Max();
            var maxCountRr = receivingReportSummary.Max();
            var maxCountCv = checkVoucherSummary.Max();
            var maxCountJv = journalVoucherSummary.Max();

            var maxCounts = new List<int>
            {
                maxCountSalesInvoice,
                maxCountServiceInvoice,
                maxCountCr,
                maxCountDm,
                maxCountCm,
                maxCountPo,
                maxCountRr,
                maxCountCv,
                maxCountJv
            };

            var overallMaxValue = maxCounts.Max();

            #endregion -- Query of length to change the range of graph --

            #region -- query that count the total record in each master file --

            var totalCustomers = await _dbContext.Customers.ToListAsync(cancellationToken);
            var totalProducts = await _dbContext.Products.ToListAsync(cancellationToken);
            var totalServices = await _dbContext.Services.ToListAsync(cancellationToken);
            var totalSuppliers = await _dbContext.Suppliers.ToListAsync(cancellationToken);
            var totalBankAccounts = await _dbContext.BankAccounts.ToListAsync(cancellationToken);
            var totalChartOfAccount = await _dbContext.ChartOfAccounts.ToListAsync(cancellationToken);

            #endregion -- query that count the total record in each master file --

            #region -- ViewModel --

            var viewModel = new HomePageViewModel
            {
                SalesInvoice = salesInvoiceSummary,
                ServiceInvoice = serviceInvoiceSummary,
                CollectionReceipt = collectionReceiptSummary,
                DebitMemo = debitMemoSummary,
                CreditMemo = creditMemoSummary,
                PurchaseOrder = purchaseOrderSummary,
                ReceivingReport = receivingReportSummary,
                CheckVoucher = checkVoucherSummary,
                JournalVoucher = journalVoucherSummary,
                OverallMaxValue = overallMaxValue,
                Customers = totalCustomers.Count(),
                Products = totalProducts.Count(),
                Services = totalServices.Count(),
                Suppliers = totalSuppliers.Count(),
                BankAccounts = totalBankAccounts.Count(),
                ChartOfAccount = totalChartOfAccount.Count(),
            };

            return View(viewModel);

            #endregion -- ViewModel --
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
