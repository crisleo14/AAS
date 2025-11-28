using Accounting_System.Data;
using Accounting_System.Models.ViewModels;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.Text;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ReportRepo _reportRepo;
        private readonly ChartOfAccountRepo _chartOfAccountRepo;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly GeneralRepo _generalRepo;

        public ReportController(ApplicationDbContext dbContext, ReportRepo reportRepo,
            ChartOfAccountRepo chartOfAccountRepo,
            UserManager<IdentityUser> userManager,
            GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _reportRepo = reportRepo;
            _chartOfAccountRepo = chartOfAccountRepo;
            _userManager = userManager;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> SalesBook(CancellationToken cancellationToken)
        {
            var viewModel = new ViewModelBook
            {
                SOA = await _dbContext.ServiceInvoices
                .Where(soa => soa.IsPosted)
                .Select(soa => new SelectListItem
                {
                    Value = soa.ServiceInvoiceId.ToString(),
                    Text = soa.ServiceInvoiceNo
                })
                .ToListAsync(cancellationToken),
                SI = await _dbContext.SalesInvoices
                .Where(si => si.IsPosted)
                .Select(soa => new SelectListItem
                {
                    Value = soa.SalesInvoiceId.ToString(),
                    Text = soa.SalesInvoiceNo
                })
                .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        public async Task<IActionResult> SalesBookReport(ViewModelBook model, string? selectedDocument, string? soaList, string? siList, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom.ToString();
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    if (soaList != null || siList != null)
                    {
                        return RedirectToAction(nameof(TransactionReportsInSOA), new { soaList, siList });
                    }

                    var salesBook = await _reportRepo.GetSalesBooksAsync(model.DateFrom, model.DateTo, selectedDocument, cancellationToken);
                    var lastRecord = salesBook.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }
                    ViewBag.SelectedDocument = selectedDocument;

                    return View(salesBook);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(SalesBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(SalesBook));
        }

        public async Task<IActionResult> TransactionReportsInSOA(int? siList, int? soaList, CancellationToken cancellationToken)
        {
            ViewBag.SIList = siList;
            ViewBag.SOAList = soaList;
            var id = siList ?? soaList;
            var sales = await _dbContext
                .SalesBooks
                .Where(s => s.DocumentId == id)
                .ToListAsync(cancellationToken);

            return View(sales);
        }

        public IActionResult CashReceiptBook()
        {
            return View();
        }

        public async Task<IActionResult> CashReceiptBookReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var cashReceiptBooks = await _reportRepo.GetCashReceiptBooks(model.DateFrom, model.DateTo, cancellationToken);
                    var lastRecord = cashReceiptBooks.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    return View(cashReceiptBooks);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(CashReceiptBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(CashReceiptBook));
        }

        public async Task<IActionResult> PurchaseBook(CancellationToken cancellationToken)
        {
            var viewModel = new ViewModelBook
            {
                PO = await _dbContext.PurchaseOrders
                .Where(po => po.IsPosted)
                .Select(po => new SelectListItem
                {
                    Value = po.PurchaseOrderId.ToString(),
                    Text = po.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        public async Task<IActionResult> PurchaseBookReport(ViewModelBook model, string? selectedFiltering, string? poListFrom, string? poListTo, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    if (poListFrom != null && poListTo != null)
                    {
                        return RedirectToAction(nameof(POLiquidationPerPO), new { poListFrom, poListTo });
                    }
                    else if (poListFrom == null && poListTo != null || poListFrom != null && poListTo == null)
                    {
                        TempData["error"] = "Please fill the two select list in PO Liquidation Per PO, lowest to highest";
                        return RedirectToAction(nameof(PurchaseBook));
                    }

                    if (selectedFiltering == "UnpostedRR" || selectedFiltering == "POLiquidation")
                    {
                        return RedirectToAction(nameof(GetRR), new { model.DateFrom, model.DateTo, selectedFiltering });
                    }

                    var purchaseOrders = await _reportRepo.GetPurchaseBooks(model.DateFrom, model.DateTo, selectedFiltering, cancellationToken);
                    var lastRecord = purchaseOrders.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }
                    ViewBag.SelectedFiltering = selectedFiltering;

                    return View(purchaseOrders);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(PurchaseBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(PurchaseBook));
        }

        public async Task<IActionResult> GetRR(DateOnly? dateFrom, DateOnly? dateTo, string selectedFiltering, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = dateFrom;
            ViewBag.DateTo = dateTo;
            ViewBag.SelectedFiltering = selectedFiltering;

            if (dateFrom == null && dateTo == null)
            {
                TempData["error"] = "Please input Date From and Date To";
                return RedirectToAction(nameof(PurchaseBook));
            }
            else if (dateFrom == null)
            {
                TempData["error"] = "Please input Date To";
                return RedirectToAction(nameof(PurchaseBook));
            }

            var receivingReport = await _reportRepo.GetReceivingReport(dateFrom, dateTo, selectedFiltering, cancellationToken);
            return View(receivingReport);
        }

        public async Task<IActionResult> POLiquidationPerPO(int? poListFrom, int? poListTo, CancellationToken cancellationToken)
        {
            var from = poListFrom;
            var to = poListTo;

            if (poListFrom > poListTo)
            {
                TempData["error"] = "Please input lowest to highest PO#!";
                return RedirectToAction(nameof(PurchaseBook));
            }

            var po = await _dbContext
                 .ReceivingReports
                 .Include(rr => rr.PurchaseOrder)
                 .ThenInclude(po => po!.Supplier)
                 .Include(rr => rr.PurchaseOrder)
                 .ThenInclude(po => po!.Product)
                 .Where(rr => rr.POId >= from && rr.POId <= to && rr.IsPosted)
                 .OrderBy(rr => rr.POId)
                 .ToListAsync(cancellationToken);
            return View(po);
        }

        public IActionResult InventoryBook()
        {
            return View();
        }

        public async Task<IActionResult> InventoryBookReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateTo.AddDays(-model.DateTo.Day + 1);
                    ViewBag.DateFrom = dateFrom;
                    var inventoryBooks = await _reportRepo.GetInventoryBooks(dateFrom, model.DateTo, cancellationToken);
                    var lastRecord = inventoryBooks.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.Date;
                    }

                    return View(inventoryBooks);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(InventoryBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(InventoryBook));
        }

        public IActionResult GeneralLedgerBook()
        {
            return View();
        }

        public async Task<IActionResult> GeneralLedgerBookReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var inventoryBooks = await _reportRepo.GetGeneralLedgerBooks(model.DateFrom, model.DateTo, cancellationToken);
                    var lastRecord = inventoryBooks.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    return View(inventoryBooks);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(GeneralLedgerBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(GeneralLedgerBook));
        }

        public IActionResult DisbursementBook()
        {
            return View();
        }

        public async Task<IActionResult> DisbursementBookReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var disbursementBooks = await _reportRepo.GetDisbursementBooks(model.DateFrom, model.DateTo, cancellationToken);
                    var lastRecord = disbursementBooks.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    return View(disbursementBooks);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(DisbursementBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(DisbursementBook));
        }

        public IActionResult JournalBook()
        {
            return View();
        }

        public async Task<IActionResult> JournalBookReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var journalBooks = await _reportRepo.GetJournalBooks(model.DateFrom, model.DateTo, cancellationToken);
                    var lastRecord = journalBooks.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    return View(journalBooks);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(JournalBook));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(JournalBook));
        }

        public IActionResult AuditTrail()
        {
            return View();
        }

        public async Task<IActionResult> AuditTrailReport(ViewModelBook model, CancellationToken cancellationToken)
        {
            ViewBag.DateFrom = model.DateFrom;
            ViewBag.DateTo = model.DateTo;
            if (ModelState.IsValid)
            {
                try
                {
                    var auditTrail = await _reportRepo.GetAuditTrails(model.DateFrom, model.DateTo, cancellationToken);
                    var lastRecord = auditTrail.LastOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.Date;
                    }

                    return View(auditTrail);
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(AuditTrail));
                }
            }

            TempData["error"] = "Please input date from";
            return RedirectToAction(nameof(AuditTrail));
        }

        // public async Task<IActionResult> CustomerProfile(CancellationToken cancellationToken)
        // {
        //     var customers = await _reportRepo.GetCustomersAsync(cancellationToken);
        //
        //     return View(customers);
        // }

        public async Task<IActionResult> ProductList(CancellationToken cancellationToken)
        {
            var products = await _reportRepo.GetProductsAsync(cancellationToken);

            return View(products);
        }

        public IActionResult SummaryOfChartOfAccount(CancellationToken cancellationToken)
        {
            try
            {
                var summary = _chartOfAccountRepo
                .GetSummaryReportView(cancellationToken);

                return View(summary.OrderBy(x => x.AccountNumber));
            }
            catch (Exception)
            {
                return BadRequest();
            }
        }
        //Generate as .txt file
        #region -- Function truncate --

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length > maxLength ? value.Substring(0, maxLength) : value;
        }

        #endregion

        #region -- Generate Audit Trail .Txt File --

        public async Task<IActionResult> GenerateAuditTrailTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
                    var auditTrail = await _reportRepo.GetAuditTrails(model.DateFrom, model.DateTo, cancellationToken);
                    if (auditTrail.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(AuditTrail));
                    }
                    var lastRecord = auditTrail.LastOrDefault();
                    var firstRecord = auditTrail.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.Date;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Audit Trail Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{auditTrail.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{"N/A"}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description"}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-8}\t{"1"}\t{"25"}\t{"25"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy hh:mm:ss tt"), 25)}");
                    fileContent.AppendLine($"{"Username"}\t{"Username"}\t{"27"}\t{"46"}\t{"20"}\t{Truncate(firstRecord.Username, 20)}");
                    fileContent.AppendLine($"{"MachineName"}\t{"Machine Name"}\t{"48"}\t{"77"}\t{"30"}\t{Truncate(firstRecord.MachineName, 30)}");
                    fileContent.AppendLine($"{"Activity"}\t{"Activity"}\t{"79"}\t{"278"}\t{"200"}\t{Truncate(firstRecord.Activity, 200)}");
                    fileContent.AppendLine($"{"DocumentType"}\t{"Document Type"}\t{"280"}\t{"299"}\t{"20"}\t{Truncate(firstRecord.DocumentType, 20)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("AUDIT TRAIL REPORT");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-25}\t{"Username",-20}\t{"Machine Name",-30}\t{"Activity",-200}\t{"Document Type",-20}");

                    // Generate the records
                    foreach (var record in auditTrail)
                    {
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy hh:mm:ss tt"), 25),-25}\t" +
                                               $"{Truncate(record.Username, 20),-20}\t" +
                                               $"{Truncate(record.MachineName, 30),-30}\t" +
                                               $"{Truncate(record.Activity, 200),-200}\t" +
                                               $"{Truncate(record.DocumentType, 20),-20}"
                                               );
                    }

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "AuditTrailReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(AuditTrail));
                }
            }
            return RedirectToAction(nameof(AuditTrail));
        }

        #endregion -- Generate Audit Trail .Txt File --

        #region -- Generate Disbursement Book .Txt File --

        public async Task<IActionResult> GenerateDisbursementBookTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    var disbursementBooks = await _reportRepo.GetDisbursementBooks(model.DateFrom, model.DateTo, cancellationToken);
                    if (disbursementBooks.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(DisbursementBook));
                    }
                    var totalDebit = disbursementBooks.Sum(db => db.Debit);
                    var totalCredit = disbursementBooks.Sum(db => db.Credit);
                    var lastRecord = disbursementBooks.LastOrDefault();
                    var firstRecord = disbursementBooks.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Disbursement Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{disbursementBooks.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalDebit}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description",-18}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-18}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"CVNo",-8}\t{"CV No",-18}\t{"12"}\t{"23"}\t{"12"}\t{Truncate(firstRecord.CVNo, 12)}");
                    fileContent.AppendLine($"{"Payee",-8}\t{"Payee",-18}\t{"25"}\t{"124"}\t{"100"}\t{Truncate(firstRecord.Payee, 100)}");
                    fileContent.AppendLine($"{"Particulars"}\t{"Particulars",-18}\t{"126"}\t{"325"}\t{"200"}\t{Truncate(firstRecord.Particulars, 200)}");
                    fileContent.AppendLine($"{"Bank",-8}\t{"Bank",-18}\t{"327"}\t{"336"}\t{"10"}\t{Truncate(firstRecord.Bank, 10)}");
                    fileContent.AppendLine($"{"CheckNo",-8}\t{"Check No",-18}\t{"338"}\t{"357"}\t{"20"}\t{Truncate(firstRecord.CheckNo, 20)}");
                    fileContent.AppendLine($"{"CheckDate"}\t{"Check Date",-18}\t{"359"}\t{"368"}\t{"10"}\t{Truncate(firstRecord.CheckDate, 10)}");
                    fileContent.AppendLine($"{"ChartOfAccount"}\t{"Chart Of Account"}\t{"370"}\t{"469"}\t{"100"}\t{Truncate(firstRecord.ChartOfAccount, 100)}");
                    fileContent.AppendLine($"{"Debit",-8}\t{"Debit",-18}\t{"471"}\t{"488"}\t{"18"}\t{Truncate(firstRecord.Debit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Credit",-8}\t{"Credit",-18}\t{"490"}\t{"507"}\t{"18"}\t{Truncate(firstRecord.Credit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("DISBURSEMENT BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"CV No",-12}\t{"Payee",-100}\t{"Particulars",-200}\t{"Bank",-10}\t{"Check No",-20}\t{"Check Date",-10}\t{"Chart Of Account",-100}\t{"Debit",18}\t{"Credit",18}");

                    // Generate the records
                    foreach (var record in disbursementBooks)
                    {
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.CVNo, 12),-12}\t" +
                                               $"{Truncate(record.Payee, 100),-100}\t" +
                                               $"{Truncate(record.Particulars, 200),-200}\t" +
                                               $"{Truncate(record.Bank, 10),-10}\t" +
                                               $"{Truncate(record.CheckNo, 20),-20}\t" +
                                               $"{Truncate(record.CheckDate, 10),-10}\t" +
                                               $"{Truncate(record.ChartOfAccount, 100),-100}\t" +
                                               $"{Truncate(record.Debit.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Credit.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 547));
                    fileContent.AppendLine($"{"",-10}\t{"",-12}\t{"",-100}\t{"",-200}\t{"",-10}\t{"",-20}\t{"",-10}\t{"TOTAL:",-100}\t{totalDebit,18}\t{totalCredit,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine("Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine("Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "DisbursementBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(DisbursementBook));
                }
            }
            return RedirectToAction(nameof(DisbursementBook));
        }

        #endregion -- Generate Disbursement Book .Txt File --

        #region -- Generate Cash Receipt Book .Txt File --

        public async Task<IActionResult> GenerateCashReceiptBookTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    var cashReceiptBooks = await _reportRepo.GetCashReceiptBooks(model.DateFrom, model.DateTo, cancellationToken);
                    if (cashReceiptBooks.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(CashReceiptBook));
                    }
                    var totalDebit = cashReceiptBooks.Sum(crb => crb.Debit);
                    var totalCredit = cashReceiptBooks.Sum(crb => crb.Credit);
                    var lastRecord = cashReceiptBooks.LastOrDefault();
                    var firstRecord = cashReceiptBooks.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Cash Receipt Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{cashReceiptBooks.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalDebit}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description",-18}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-18}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"RefNo",-8}\t{"Ref No.",-18}\t{"12"}\t{"23"}\t{"12"}\t{Truncate(firstRecord.RefNo, 12)}");
                    fileContent.AppendLine($"{"CustomerName"}\t{"Customer Name",-18}\t{"25"}\t{"124"}\t{"100"}\t{Truncate(firstRecord.CustomerName,100)}");
                    fileContent.AppendLine($"{"Bank",-8}\t{"Bank",-18}\t{"126"}\t{"225"}\t{"100"}\t{Truncate(firstRecord.Bank!, 100)}");
                    fileContent.AppendLine($"{"CheckNo",-8}\t{"Check No.",-18}\t{"227"}\t{"246"}\t{"20"}\t{Truncate(firstRecord.CheckNo!, 20)}");
                    fileContent.AppendLine($"{"COA",-8}\t{"Chart Of Account",-18}\t{"248"}\t{"347"}\t{"100"}\t{Truncate(firstRecord.COA, 100)}");
                    fileContent.AppendLine($"{"Particulars"}\t{"Particulars",-18}\t{"349"}\t{"548"}\t{"200"}\t{Truncate(firstRecord.Particulars, 200)}");
                    fileContent.AppendLine($"{"Debit",-8}\t{"Debit",-18}\t{"550"}\t{"567"}\t{"18"}\t{Truncate(firstRecord.Debit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Credit",-8}\t{"Credit",-18}\t{"569"}\t{"586"}\t{"18"}\t{Truncate(firstRecord.Credit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("CASH RECEIPT BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"Ref No.",-12}\t{"Customer Name",-100}\t{"Bank",-100}\t{"Check No.",-20}\t{"Chart Of Account",-100}\t{"Particulars",-200}\t{"Debit",18}\t{"Credit",18}");

                    // Generate the records
                    foreach (var record in cashReceiptBooks)
                    {
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.RefNo, 12),-12}\t" +
                                               $"{Truncate(record.CustomerName, 100),-100}\t" +
                                               $"{Truncate(record.Bank!, 100),-100}\t" +
                                               $"{Truncate(record.CheckNo!, 20),-20}\t" +
                                               $"{Truncate(record.COA, 100),-100}\t" +
                                               $"{Truncate(record.Particulars, 200),-200}\t" +
                                               $"{Truncate(record.Debit.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Credit.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 623));
                    fileContent.AppendLine($"{"",-10}\t{"",-12}\t{"",-100}\t{"",-100}\t{"",-20}\t{"",-100}\t{"TOTAL:",200}\t{totalDebit,18}\t{totalCredit,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "CashReceiptBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(CashReceiptBook));
                }
            }
            return RedirectToAction(nameof(CashReceiptBook));
        }

        #endregion -- Generate Cash Receipt Book .Txt File --

        #region -- Generate General Ledger Book .Txt File --

        public async Task<IActionResult> GenerateGeneralLedgerBookTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    var generalBooks = await _reportRepo.GetGeneralLedgerBooks(model.DateFrom, model.DateTo, cancellationToken);
                    if (generalBooks.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(GeneralLedgerBook));
                    }
                    var totalDebit = generalBooks.Sum(gb => gb.Debit);
                    var totalCredit = generalBooks.Sum(gb => gb.Credit);
                    var lastRecord = generalBooks.LastOrDefault();
                    var firstRecord = generalBooks.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: General Ledger Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{generalBooks.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalDebit}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description"}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-8}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"Reference"}\t{"Reference"}\t{"12"}\t{"23"}\t{"12"}\t{Truncate(firstRecord.Reference, 12)}");
                    fileContent.AppendLine($"{"Description"}\t{"Description"}\t{"25"}\t{"224"}\t{"200"}\t{Truncate(firstRecord.Description, 200)}");
                    fileContent.AppendLine($"{"AccountTitle"}\t{"Account Title"}\t{"226"}\t{"275"}\t{"50"}\t{Truncate(firstRecord.AccountNo + " " + firstRecord.AccountTitle, 50)}");
                    fileContent.AppendLine($"{"Debit",-8}\t{"Debit",-8}\t{"277"}\t{"294"}\t{"18"}\t{Truncate(firstRecord.Debit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Credit",-8}\t{"Credit",-8}\t{"296"}\t{"313"}\t{"18"}\t{Truncate(firstRecord.Credit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("GENERAL LEDGER BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"Reference",-12}\t{"Description",-200}\t{"Account Title",-50}\t{"Debit",18}\t{"Credit",18}");

                    // Generate the records
                    foreach (var record in generalBooks)
                    {
                        fileContent.AppendLine($"{record.Date.ToString("MM/dd/yyyy"),-10}\t" +
                                               $"{Truncate(record.Reference, 12),-12}\t" +
                                               $"{Truncate(record.Description, 200),-200}\t" +
                                               $"{Truncate(record.AccountNo + " " + record.AccountTitle, 50),-50}\t" +
                                               $"{Truncate(record.Debit.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Credit.ToString(CS.Four_Decimal_Format), 18),18}");
                    }
                    fileContent.AppendLine(new string('-', 340));
                    fileContent.AppendLine($"{"",-10}\t{"",-12}\t{"",-200}\t{"TOTAL:",50}\t{totalDebit,18}\t{totalCredit,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "GeneralLedgerBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(GeneralLedgerBook));
                }
            }
            return RedirectToAction(nameof(GeneralLedgerBook));
        }

        #endregion -- Generate General Ledger Book .Txt File --

        #region -- Generate Inventory Book .Txt File --

        public async Task<IActionResult> GenerateInventoryBookTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateTo = model.DateTo;
                    var dateFrom = dateTo.AddDays(-dateTo.Day + 1);
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    var inventoryBooks = await _reportRepo.GetInventoryBooks(dateFrom, dateTo, cancellationToken);
                    if (inventoryBooks.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(InventoryBook));
                    }
                    var totalAmount = inventoryBooks.Sum(ib => ib.Total);
                    var totalQuantity = inventoryBooks.Sum(ib => ib.Quantity);
                    var lastRecord = inventoryBooks.LastOrDefault();
                    var firstRecord = inventoryBooks.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.Date;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Inventory Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{inventoryBooks.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalAmount}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description"}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-8}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"ProductCode",-8}\t{"Product Code",-8}\t{"12"}\t{"31"}\t{"20"}\t{Truncate(firstRecord.Product.ProductCode!, 20)}");
                    fileContent.AppendLine($"{"ProductName",-8}\t{"Product Name",-8}\t{"33"}\t{"82"}\t{"50"}\t{Truncate(firstRecord.Product.ProductName, 50)}");
                    fileContent.AppendLine($"{"Unit",-8}\t{"Unit",-8}\t{"84"}\t{"85"}\t{"2"}\t{Truncate(firstRecord.Product.ProductUnit, 2)}");
                    fileContent.AppendLine($"{"Quantity",-8}\t{"Quantity",-8}\t{"87"}\t{"104"}\t{"18"}\t{Truncate(firstRecord.Quantity.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Price",-8}\t{"Price Per Unit",-8}\t{"106"}\t{"123"}\t{"18"}\t{Truncate(firstRecord.Cost.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Amount",-8}\t{"Amount",-8}\t{"125"}\t{"142"}\t{"18"}\t{Truncate(firstRecord.Total.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("INVENTORY BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"Product Code",-20}\t{"Product Name",-50}\t{"Unit",-2}\t{"Quantity",18}\t{"Price Per Unit",18}\t{"Amount",18}");

                    var totalPriceUnitAmount = 0m;
                    // Generate the records
                    foreach (var record in inventoryBooks)
                    {
                        var getLastRecordCost = record.Cost;
                        if (totalAmount != 0 && totalQuantity != 0)
                        {
                            totalPriceUnitAmount = totalAmount / totalQuantity;
                        }
                        else
                        {
                            totalPriceUnitAmount = getLastRecordCost;
                        }
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.Product.ProductCode!, 20),-20}\t" +
                                               $"{Truncate(record.Product.ProductCode!, 50),-50}\t" +
                                               $"{Truncate(record.Unit, 2),-2}\t" +
                                               $"{Truncate(record.Quantity.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Cost.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Total.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 171));
                    fileContent.AppendLine($"{"",-10}\t{"",-20}\t{"",-50}\t{"TOTAL:",2}\t{totalQuantity,18}\t{totalPriceUnitAmount.ToString("N2"),18}\t{totalAmount,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "InventoryBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(InventoryBook));
                }
            }
            return RedirectToAction(nameof(InventoryBook));
        }

        #endregion -- Generate Inventory Book .Txt File --

        #region -- Generate Journal Book .Txt File --

        public async Task<IActionResult> GenerateJournalBookTxtFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    var journalBooks = await _reportRepo.GetJournalBooks(model.DateFrom, model.DateTo, cancellationToken);
                    if (journalBooks.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(JournalBook));
                    }
                    var lastRecord = journalBooks.LastOrDefault();
                    var firstRecord = journalBooks.FirstOrDefault();
                    var totalDebit = journalBooks.Sum(jb => jb.Debit);
                    var totalCredit = journalBooks.Sum(jb => jb.Credit);
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Journal Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{journalBooks.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalDebit}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name"}\t{"Description"}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-8}\t{"Date",-8}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"Reference",-8}\t{"Reference",-8}\t{"12"}\t{"23"}\t{"12"}\t{Truncate(firstRecord.Reference, 12)}");
                    fileContent.AppendLine($"{"Description",-8}\t{"Description",-8}\t{"25"}\t{"224"}\t{"200"}\t{Truncate(firstRecord.Description, 200)}");
                    fileContent.AppendLine($"{"AccountTitle",-8}\t{"Account Title",-8}\t{"226"}\t{"275"}\t{"50"}\t{Truncate(firstRecord.AccountTitle, 50)}");
                    fileContent.AppendLine($"{"Debit",-8}\t{"Debit",-8}\t{"277"}\t{"294"}\t{"18"}\t{Truncate(firstRecord.Debit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Credit",-8}\t{"Credit",-8}\t{"296"}\t{"313"}\t{"18"}\t{Truncate(firstRecord.Credit.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("JOURNAL BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"Reference",-12}\t{"Description",-200}\t{"Account Title",-50}\t{"Debit",18}\t{"Credit",18}");

                    // Generate the records
                    foreach (var record in journalBooks)
                    {
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.Reference, 12),-12}\t" +
                                               $"{Truncate(record.Description, 200),-200}\t" +
                                               $"{Truncate(record.AccountTitle, 50),-50}\t" +
                                               $"{Truncate(record.Debit.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Credit.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 340));
                    fileContent.AppendLine($"{"",-10}\t{"",-12}\t{"",-200}\t{"TOTAL:",50}\t{totalDebit,18}\t{totalCredit,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "JournalBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(JournalBook));
                }
            }
            return RedirectToAction(nameof(JournalBook));
        }

        #endregion -- Generate Journal Book .Txt File --

        #region -- Generate Purchase Book .Txt File --

        public async Task<IActionResult> GeneratePurchaseBookTxtFile(ViewModelBook model, string? selectedFiltering, string? poListFrom, string? poListTo, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    if (poListFrom != null && poListTo != null)
                    {
                        return RedirectToAction(nameof(POLiquidationPerPO), new { poListFrom, poListTo });
                    }
                    else if (poListFrom == null && poListTo != null || poListFrom != null && poListTo == null)
                    {
                        TempData["error"] = "Please fill the two select list in PO Liquidation Per PO, lowest to highest";
                        return RedirectToAction(nameof(PurchaseBook));
                    }

                    if (selectedFiltering == "UnpostedRR" || selectedFiltering == "POLiquidation")
                    {
                        return RedirectToAction(nameof(GetRR), new { model.DateFrom, model.DateTo, selectedFiltering });
                    }

                    var purchaseOrders = await _reportRepo.GetPurchaseBooks(model.DateFrom, model.DateTo, selectedFiltering, cancellationToken);
                    if (purchaseOrders.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(PurchaseBook));
                    }
                    var totalAmount = purchaseOrders.Sum(sb => sb.Amount);
                    var totalVatAmount = purchaseOrders.Sum(sb => sb.VatAmount);
                    var totalWhtAmount = purchaseOrders.Sum(sb => sb.WhtAmount);
                    var totalNetPurchases = purchaseOrders.Sum(sb => sb.NetPurchases);
                    var lastRecord = purchaseOrders.LastOrDefault();
                    var firstRecord = purchaseOrders.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }
                    ViewBag.SelectedFiltering = selectedFiltering;

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Purchase Journal Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{purchaseOrders.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalAmount}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name",-18}\t{"Description",-18}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"Date",-18}\t{"Date",-18}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.Date.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"SupplierName",-18}\t{"Supplier Name",-18}\t{"12"}\t{"61"}\t{"50"}\t{Truncate(firstRecord.SupplierName, 50)}");
                    fileContent.AppendLine($"{"SupplierTin",-18}\t{"Supplier TIN",-18}\t{"63"}\t{"82"}\t{"20"}\t{Truncate(firstRecord.SupplierTin, 20)}");
                    fileContent.AppendLine($"{"SupplierAddress",-18}\t{"Supplier Address",-18}\t{"84"}\t{"283"}\t{"200"}\t{Truncate(firstRecord.SupplierAddress, 200)}");
                    fileContent.AppendLine($"{"PONo",-18}\t{"PO No.",-18}\t{"285"}\t{"296"}\t{"12"}\t{Truncate(firstRecord.PONo, 12)}");
                    fileContent.AppendLine($"{"DocumentNo",-18}\t{"Document No",-18}\t{"298"}\t{"309"}\t{"12"}\t{Truncate(firstRecord.DocumentNo, 12)}");
                    fileContent.AppendLine($"{"Description",-18}\t{"Description",-18}\t{"311"}\t{"360"}\t{"50"}\t{Truncate(firstRecord.Description, 50)}");
                    fileContent.AppendLine($"{"Amount",-18}\t{"Amount",-18}\t{"362"}\t{"379"}\t{"18"}\t{Truncate(firstRecord.Amount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"VatAmount",-18}\t{"Vat Amount",-18}\t{"381"}\t{"398"}\t{"18"}\t{Truncate(firstRecord.VatAmount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"DefAmount",-18}\t{"Def VAT Amount",-18}\t{"400"}\t{"417"}\t{"18"}\t{0.00}");
                    fileContent.AppendLine($"{"WhtAmount",-18}\t{"WHT Amount",-18}\t{"419"}\t{"436"}\t{"18"}\t{Truncate(firstRecord.WhtAmount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"NetPurchases",-18}\t{"Net Purchases",-18}\t{"438"}\t{"455"}\t{"18"}\t{Truncate(firstRecord.NetPurchases.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("PURCHASE BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Date",-10}\t{"Supplier Name",-50}\t{"Supplier TIN",-20}\t{"Supplier Address",-200}\t{"PO No.",-12}\t{"Document No",-12}\t{"Description",-50}\t{"Amount",18}\t{"Vat Amount",18}\t{"Def VAT Amount",18}\t{"WHT Amount",18}\t{"Net Purchases",18}");

                    // Generate the records
                    foreach (var record in purchaseOrders)
                    {
                        fileContent.AppendLine($"{Truncate(record.Date.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.SupplierName, 50),-50}\t" +
                                               $"{Truncate(record.SupplierTin, 20),-20}\t" +
                                               $"{Truncate(record.SupplierAddress, 200),-200}\t" +
                                               $"{Truncate(record.PONo, 12),-12}\t" +
                                               $"{Truncate(record.DocumentNo, 12),-12}\t" +
                                               $"{Truncate(record.Description, 50),-50}\t" +
                                               $"{Truncate(record.Amount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.VatAmount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{0.00m,18}\t" +
                                               $"{Truncate(record.WhtAmount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.NetPurchases.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 507));
                    fileContent.AppendLine($"{"",-10}\t{"",-50}\t{"",-20}\t{"",-200}\t{"",-12}\t{"",-12}\t{"TOTAL:",50}\t{totalAmount,18}\t{totalVatAmount,18}\t{0.00m,18}\t{totalWhtAmount,18}\t{totalNetPurchases,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine($"Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine($"Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "PurchaseBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(PurchaseBook));
                }
            }
            return RedirectToAction(nameof(PurchaseBook));
        }

        #endregion -- Generate Purchase Book .Txt File --

        #region -- Generate Sales Book .Txt File --

        public async Task<IActionResult> GenerateSalesBookTxtFile(ViewModelBook model, string? selectedDocument, string? soaList, string? siList, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    var dateFrom = model.DateFrom;
                    var dateTo = model.DateTo;
                    var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

                    if (soaList != null || siList != null)
                    {
                        return RedirectToAction(nameof(TransactionReportsInSOA), new { soaList, siList });
                    }

                    var salesBook = await _reportRepo.GetSalesBooksAsync(model.DateFrom, model.DateTo, selectedDocument, cancellationToken);
                    if (salesBook.Count == 0)
                    {
                        TempData["error"] = "No Record Found";
                        return RedirectToAction(nameof(SalesBook));
                    }
                    var totalAmount = salesBook.Sum(sb => sb.Amount);
                    var totalVatAmount = salesBook.Sum(sb => sb.VatAmount);
                    var totalVatableSales = salesBook.Sum(sb => sb.VatableSales);
                    var totalVatExemptSales = salesBook.Sum(sb => sb.VatExemptSales);
                    var totalZeroRatedSales = salesBook.Sum(sb => sb.ZeroRated);
                    var totalDiscount = salesBook.Sum(sb => sb.Discount);
                    var totalNetSales = salesBook.Sum(sb => sb.NetSales);
                    var lastRecord = salesBook.LastOrDefault();
                    var firstRecord = salesBook.FirstOrDefault();
                    if (lastRecord != null)
                    {
                        ViewBag.LastRecord = lastRecord.CreatedDate;
                    }
                    ViewBag.SelectedDocument = selectedDocument;

                    var fileContent = new StringBuilder();

                    fileContent.AppendLine($"TAXPAYER'S NAME: Filpride Resources Inc.");
                    fileContent.AppendLine($"TIN: 000-216-589-00000");
                    fileContent.AppendLine($"ADDRESS: 57 Westgate Office, Sampson Road, CBD, Subic Bay Freeport Zone, Kalaklan, Olongapo City, 2200 Zambales, Philippines");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"Accounting System: Accounting Administration System");
                    fileContent.AppendLine($"Acknowledgement Certificate Control No.: {CS.ACCN}");
                    fileContent.AppendLine($"Date Issued: {CS.DateIssued}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("Accounting Books File Attributes/Layout Definition");
                    fileContent.AppendLine("File Name: Sales Book Report");
                    fileContent.AppendLine("File Type: Text File");
                    fileContent.AppendLine($"{"Number of Records: ",-35}{salesBook.Count}");
                    fileContent.AppendLine($"{"Amount Field Control Total: ",-35}{totalAmount}");
                    fileContent.AppendLine($"{"Period Covered: ",-35}{dateFrom}{" to "}{dateTo} ");
                    fileContent.AppendLine($"{"Transaction cut-off Date & Time: ",-35}{ViewBag.LastRecord}");
                    fileContent.AppendLine($"{"Extracted By: ",-35}{extractedBy!.ToUpper()}");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Field Name",-18}\t{"Description",-18}\t{"From"}\t{"To"}\t{"Length"}\t{"Example"}");
                    fileContent.AppendLine($"{"TransactionDate",-18}\t{"Tran. Date",-18}\t{"1"}\t{"10"}\t{"10"}\t{Truncate(firstRecord!.TransactionDate.ToString("MM/dd/yyyy"), 10)}");
                    fileContent.AppendLine($"{"SerialNo",-18}\t{"Serial Number",-18}\t{"12"}\t{"23"}\t{"12"}\t{Truncate(firstRecord.SerialNo, 12)}");
                    fileContent.AppendLine($"{"CustomerName",-18}\t{"Customer Name",-18}\t{"25"}\t{"124"}\t{"100"}\t{Truncate(firstRecord.SoldTo, 100)}");
                    fileContent.AppendLine($"{"TinNo",-18}\t{"Tin#",-18}\t{"126"}\t{"145"}\t{"20"}\t{Truncate(firstRecord.TinNo, 20)}");
                    fileContent.AppendLine($"{"Address",-18}\t{"Address",-18}\t{"147"}\t{"346"}\t{"200"}\t{Truncate(firstRecord.Address, 200)}");
                    fileContent.AppendLine($"{"Description",-18}\t{"Description",-18}\t{"348"}\t{"397"}\t{"50"}\t{Truncate(firstRecord.Description, 50)}");
                    fileContent.AppendLine($"{"Amount",-18}\t{"Amount",-18}\t{"399"}\t{"416"}\t{"18"}\t{Truncate(firstRecord.Amount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"VatAmount",-18}\t{"Vat Amount",-18}\t{"418"}\t{"435"}\t{"18"}\t{Truncate(firstRecord.VatAmount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"VatableSales",-18}\t{"Vatable Sales",-18}\t{"437"}\t{"454"}\t{"18"}\t{Truncate(firstRecord.VatableSales.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"VatExemptSales",-18}\t{"Vat-Exempt Sales",-18}\t{"456"}\t{"473"}\t{"18"}\t{Truncate(firstRecord.VatExemptSales.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"ZeroRated",-18}\t{"Zero-Rated Sales",-18}\t{"475"}\t{"492"}\t{"18"}\t{Truncate(firstRecord.ZeroRated.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"Discount",-18}\t{"Discount",-18}\t{"494"}\t{"511"}\t{"18"}\t{Truncate(firstRecord.Discount.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine($"{"NetSales",-18}\t{"Net Sales",-18}\t{"513"}\t{"530"}\t{"18"}\t{Truncate(firstRecord.NetSales.ToString(CS.Four_Decimal_Format), 18)}");
                    fileContent.AppendLine();
                    fileContent.AppendLine("SALES BOOK");
                    fileContent.AppendLine();
                    fileContent.AppendLine($"{"Tran. Date",-10}\t{"Serial Number",-12}\t{"Customer Name",-100}\t{"Tin#",-20}\t{"Address",-200}\t{"Description",-50}\t{"Amount",18}\t{"Vat Amount",18}\t{"Vatable Sales",18}\t{"Vat-Exempt Sales",18}\t{"Zero-Rated Sales",18}\t{"Discount",18}\t{"Net Sales",18}");

                    // Generate the records
                    foreach (var record in salesBook)
                    {
                        fileContent.AppendLine($"{Truncate(record.TransactionDate.ToString("MM/dd/yyyy"), 10),-10}\t" +
                                               $"{Truncate(record.SerialNo, 12),-12}\t" +
                                               $"{Truncate(record.SoldTo, 100),-100}\t" +
                                               $"{Truncate(record.TinNo, 20),-20}\t" +
                                               $"{Truncate(record.Address, 200),-200}\t" +
                                               $"{Truncate(record.Description, 50),-50}\t" +
                                               $"{Truncate(record.Amount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.VatAmount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.VatableSales.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.VatExemptSales.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.ZeroRated.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.Discount.ToString(CS.Four_Decimal_Format), 18),18}\t" +
                                               $"{Truncate(record.NetSales.ToString(CS.Four_Decimal_Format), 18),18}"
                                               );
                    }
                    fileContent.AppendLine(new string('-', 587));
                    fileContent.AppendLine($"{"",-10}\t{"",-12}\t{"",-100}\t{"",-20}\t{"",-200}\t{"TOTAL:",50}\t{totalAmount,18}\t{totalVatAmount,18}\t{totalVatableSales,18}\t{totalVatExemptSales,18}\t{totalZeroRatedSales,18}\t{totalDiscount,18}\t{totalNetSales,18}");

                    fileContent.AppendLine();
                    fileContent.AppendLine("Software Name: Accounting Administration System (AAS)");
                    fileContent.AppendLine("Version: v1.1");
                    fileContent.AppendLine($"Extracted By: {extractedBy.ToUpper()}");
                    fileContent.AppendLine($"Date & Time Extracted: {@DateTime.Now.ToString("MM/dd/yyyy hh:mm tt")}");

                    // Convert the content to a byte array
                    var bytes = Encoding.UTF8.GetBytes(fileContent.ToString());

                    // Return the file to the user
                    return File(bytes, "text/plain", "SalesBookReport.txt");
                }
                catch (Exception ex)
                {
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(SalesBook));
                }
            }
            return RedirectToAction(nameof(SalesBook));
        }

        #endregion -- Generate Sales Book .Txt File --

        //Generate as .csv file.
        #region -- Generate DisbursmentBook .Csv File --

        public async Task<IActionResult> GenerateDisbursementBookCsvFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            var disbursementBooks = await _reportRepo.GetDisbursementBooks(model.DateFrom, model.DateTo, cancellationToken);
            if (disbursementBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(DisbursementBook));
            }
            var totalDebit = disbursementBooks.Sum(db => db.Debit);
            var totalCredit = disbursementBooks.Sum(db => db.Credit);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("DisbursmentBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "DISBURSEMENT BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "CV No";
            worksheet.Cells["C7"].Value = "Payee";
            worksheet.Cells["D7"].Value = "Particulars";
            worksheet.Cells["E7"].Value = "Bank";
            worksheet.Cells["F7"].Value = "Check No";
            worksheet.Cells["G7"].Value = "Check Date";
            worksheet.Cells["H7"].Value = "Chart Of Account";
            worksheet.Cells["I7"].Value = "Debit";
            worksheet.Cells["J7"].Value = "Credit";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:J7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var cv in disbursementBooks)
            {
                worksheet.Cells[row, 1].Value = cv.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = cv.CVNo;
                worksheet.Cells[row, 3].Value = cv.Payee;
                worksheet.Cells[row, 4].Value = cv.Particulars;
                worksheet.Cells[row, 5].Value = cv.Bank;
                worksheet.Cells[row, 6].Value = cv.CheckNo;
                worksheet.Cells[row, 7].Value = cv.CheckDate;
                worksheet.Cells[row, 8].Value = cv.ChartOfAccount;

                worksheet.Cells[row, 9].Value = cv.Debit;
                worksheet.Cells[row, 10].Value = cv.Credit;

                worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 8].Value = "Total ";
            worksheet.Cells[row, 9].Value = totalDebit;
            worksheet.Cells[row, 10].Value = totalCredit;

            worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 10])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 8, row, 10])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DisbursementBook.xlsx");
        }

        #endregion -- Generate DisbursmentBook .Csv File --

        #region -- Generate CashReceiptBook .Csv File --

        public async Task<IActionResult> GenerateCashReceiptBookCsvFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            var cashReceiptBooks = await _reportRepo.GetCashReceiptBooks(model.DateFrom, model.DateTo, cancellationToken);
            if (cashReceiptBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(CashReceiptBook));
            }
            var totalDebit = cashReceiptBooks.Sum(crb => crb.Debit);
            var totalCredit = cashReceiptBooks.Sum(crb => crb.Credit);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("CashReceiptBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "CASH RECEIPT BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "Ref No";
            worksheet.Cells["C7"].Value = "Customer Name";
            worksheet.Cells["D7"].Value = "Bank";
            worksheet.Cells["E7"].Value = "Check No";
            worksheet.Cells["F7"].Value = "Chart Of Account";
            worksheet.Cells["G7"].Value = "Particulars";
            worksheet.Cells["H7"].Value = "Debit";
            worksheet.Cells["I7"].Value = "Credit";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:I7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var cashReceipt in cashReceiptBooks)
            {
                worksheet.Cells[row, 1].Value = cashReceipt.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = cashReceipt.RefNo;
                worksheet.Cells[row, 3].Value = cashReceipt.CustomerName;
                worksheet.Cells[row, 4].Value = cashReceipt.Bank;
                worksheet.Cells[row, 5].Value = cashReceipt.CheckNo;
                worksheet.Cells[row, 6].Value = cashReceipt.COA;
                worksheet.Cells[row, 7].Value = cashReceipt.Particulars;

                worksheet.Cells[row, 8].Value = cashReceipt.Debit;
                worksheet.Cells[row, 9].Value = cashReceipt.Credit;

                worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 7].Value = "Total ";
            worksheet.Cells[row, 8].Value = totalDebit;
            worksheet.Cells[row, 9].Value = totalCredit;

            worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 9])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 7, row, 9])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CashReceiptBook.xlsx");
        }

        #endregion -- Generate CashReceiptBook .Csv File --

        #region -- Generate GeneralLedgerBook .Csv File --

        public async Task<IActionResult> GenerateGeneralLedgerBookCsvFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            var generalBooks = await _reportRepo.GetGeneralLedgerBooks(model.DateFrom, model.DateTo, cancellationToken);
            if (generalBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(GeneralLedgerBook));
            }
            var totalDebit = generalBooks.Sum(gb => gb.Debit);
            var totalCredit = generalBooks.Sum(gb => gb.Credit);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("GeneralLedgerBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "GENERAL LEDGER BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "Reference";
            worksheet.Cells["C7"].Value = "Description";
            worksheet.Cells["D7"].Value = "Account Title";
            worksheet.Cells["E7"].Value = "Debit";
            worksheet.Cells["F7"].Value = "Credit";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:F7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var gl in generalBooks)
            {
                worksheet.Cells[row, 1].Value = gl.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = gl.Reference;
                worksheet.Cells[row, 3].Value = gl.Description;
                worksheet.Cells[row, 4].Value = $"{gl.AccountNo} {gl.AccountTitle}";

                worksheet.Cells[row, 5].Value = gl.Debit;
                worksheet.Cells[row, 6].Value = gl.Credit;

                worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 4].Value = "Total ";
            worksheet.Cells[row, 5].Value = totalDebit;
            worksheet.Cells[row, 6].Value = totalCredit;

            worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 4, row, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "GeneralLedgerBook.xlsx");
        }

        #endregion -- Generate GeneralLedgerBook .Csv File --

        #region -- Generate InventoryBook .Csv File --

        public async Task<IActionResult> GenerateInventoryBookCsvFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            var dateTo = model.DateTo;
            var dateFrom = dateTo.AddDays(-dateTo.Day + 1);
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            var inventoryBooks = await _reportRepo.GetInventoryBooks(dateFrom, dateTo, cancellationToken);
            if (inventoryBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(InventoryBook));
            }
            var totalAmount = inventoryBooks.Sum(ib => ib.Total);
            var totalQuantity = inventoryBooks.Sum(ib => ib.Quantity);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("InventoryBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "INVENTORY BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "Product Code";
            worksheet.Cells["C7"].Value = "Product Name";
            worksheet.Cells["D7"].Value = "Product Unit";
            worksheet.Cells["E7"].Value = "Quantity";
            worksheet.Cells["F7"].Value = "Price Per Unit";
            worksheet.Cells["G7"].Value = "Total";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:G7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";
            var totalPriceUnitAmount = 0m;

            foreach (var inventory in inventoryBooks)
            {
                var getLastRecordCost = inventory.Cost;
                if (totalAmount != 0 && totalQuantity != 0)
                {
                    totalPriceUnitAmount = totalAmount / totalQuantity;
                }
                else
                {
                    totalPriceUnitAmount = getLastRecordCost;
                }
                worksheet.Cells[row, 1].Value = inventory.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = inventory.Product.ProductCode;
                worksheet.Cells[row, 3].Value = inventory.Product.ProductName;
                worksheet.Cells[row, 4].Value = inventory.Product.ProductUnit;

                worksheet.Cells[row, 5].Value = inventory.Quantity;
                worksheet.Cells[row, 6].Value = inventory.Cost;
                worksheet.Cells[row, 7].Value = inventory.Total;

                worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 7].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 4].Value = "Total ";
            worksheet.Cells[row, 5].Value = totalQuantity;
            worksheet.Cells[row, 6].Value = totalPriceUnitAmount;
            worksheet.Cells[row, 7].Value = totalAmount;

            worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 7].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 7])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 4, row, 7])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "InventoryBook.xlsx");
        }

        #endregion -- Generate InventoryBook .Csv File --

        #region -- Generate JournalBook .Csv File --

        public async Task<IActionResult> GenerateJournalBookCsvFile(ViewModelBook model, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            var journalBooks = await _reportRepo.GetJournalBooks(model.DateFrom, model.DateTo, cancellationToken);
            if (journalBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(JournalBook));
            }
            var totalDebit = journalBooks.Sum(jb => jb.Debit);
            var totalCredit = journalBooks.Sum(jb => jb.Credit);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("JournalBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "JOURNAL BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "Reference";
            worksheet.Cells["C7"].Value = "Description";
            worksheet.Cells["D7"].Value = "Account Title";
            worksheet.Cells["E7"].Value = "Debit";
            worksheet.Cells["F7"].Value = "Credit";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:F7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var jv in journalBooks)
            {
                worksheet.Cells[row, 1].Value = jv.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = jv.Reference;
                worksheet.Cells[row, 3].Value = jv.Description;
                worksheet.Cells[row, 4].Value = jv.AccountTitle;

                worksheet.Cells[row, 5].Value = jv.Debit;
                worksheet.Cells[row, 6].Value = jv.Credit;

                worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 4].Value = "Total ";
            worksheet.Cells[row, 5].Value = totalDebit;
            worksheet.Cells[row, 6].Value = totalCredit;

            worksheet.Cells[row, 5].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 6].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 4, row, 6])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "JournalBook.xlsx");
        }

        #endregion -- Generate JournalBook .Csv File --

        #region -- Generate PurchaseBook .Csv File --

        public async Task<IActionResult> GeneratePurchaseBookCsvFile(ViewModelBook model, string? selectedFiltering, string? poListFrom, string? poListTo, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            if (poListFrom != null && poListTo != null)
            {
                return RedirectToAction(nameof(POLiquidationPerPO), new { poListFrom, poListTo });
            }
            else if (poListFrom == null && poListTo != null || poListFrom != null && poListTo == null)
            {
                TempData["error"] = "Please fill the two select list in PO Liquidation Per PO, lowest to highest";
                return RedirectToAction(nameof(PurchaseBook));
            }

            if (selectedFiltering == "UnpostedRR" || selectedFiltering == "POLiquidation")
            {
                return RedirectToAction(nameof(GetRR), new { model.DateFrom, model.DateTo, selectedFiltering });
            }

            var purchaseBooks = await _reportRepo.GetPurchaseBooks(model.DateFrom, model.DateTo, selectedFiltering, cancellationToken);
            if (purchaseBooks.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(PurchaseBook));
            }
            var totalAmount = purchaseBooks.Sum(sb => sb.Amount);
            var totalVatAmount = purchaseBooks.Sum(sb => sb.VatAmount);
            var totalWhtAmount = purchaseBooks.Sum(sb => sb.WhtAmount);
            var totalNetPurchases = purchaseBooks.Sum(sb => sb.NetPurchases);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("PurchaseBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "PURCHASE BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Date";
            worksheet.Cells["B7"].Value = "Supplier Name";
            worksheet.Cells["C7"].Value = "Supplier Tin";
            worksheet.Cells["D7"].Value = "Supplier Address";
            worksheet.Cells["E7"].Value = "PO No";
            worksheet.Cells["F7"].Value = "Document No";
            worksheet.Cells["G7"].Value = "Description";
            worksheet.Cells["H7"].Value = "Amount";
            worksheet.Cells["I7"].Value = "Vat Amount";
            worksheet.Cells["J7"].Value = "Def VAT Amount";
            worksheet.Cells["K7"].Value = "WHT Amount";
            worksheet.Cells["L7"].Value = "Net Purchases";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:L7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var pb in purchaseBooks)
            {
                worksheet.Cells[row, 1].Value = pb.Date.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = pb.SupplierName;
                worksheet.Cells[row, 3].Value = pb.SupplierTin;
                worksheet.Cells[row, 4].Value = pb.SupplierAddress;
                worksheet.Cells[row, 5].Value = pb.PONo;
                worksheet.Cells[row, 6].Value = pb.DocumentNo;
                worksheet.Cells[row, 7].Value = pb.Description;
                worksheet.Cells[row, 8].Value = pb.Amount;
                worksheet.Cells[row, 9].Value = pb.VatAmount;

                worksheet.Cells[row, 11].Value = pb.WhtAmount;
                worksheet.Cells[row, 12].Value = pb.NetPurchases;

                worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 11].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 12].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 7].Value = "Total ";
            worksheet.Cells[row, 8].Value = totalAmount;
            worksheet.Cells[row, 9].Value = totalVatAmount;

            worksheet.Cells[row, 11].Value = totalWhtAmount;
            worksheet.Cells[row, 12].Value = totalNetPurchases;

            worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 11].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 12].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 12])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 7, row, 12])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "PurchaseBook.xlsx");
        }

        #endregion -- Generate PurchaseBook .Csv File --

        #region -- Generate SalesBook .Csv File --

        public async Task<IActionResult> GenerateSalesBookCsvFile(ViewModelBook model, string? selectedDocument, string? soaList, string? siList, CancellationToken cancellationToken)
        {
            var dateFrom = model.DateFrom;
            var dateTo = model.DateTo;
            var extractedBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);

            if (soaList != null || siList != null)
            {
                return RedirectToAction(nameof(TransactionReportsInSOA), new { soaList, siList });
            }

            var salesBook = await _reportRepo.GetSalesBooksAsync(model.DateFrom, model.DateTo, selectedDocument, cancellationToken);
            if (salesBook.Count == 0)
            {
                TempData["error"] = "No Record Found";
                return RedirectToAction(nameof(SalesBook));
            }
            var totalAmount = salesBook.Sum(sb => sb.Amount);
            var totalVatAmount = salesBook.Sum(sb => sb.VatAmount);
            var totalVatableSales = salesBook.Sum(sb => sb.VatableSales);
            var totalVatExemptSales = salesBook.Sum(sb => sb.VatExemptSales);
            var totalZeroRatedSales = salesBook.Sum(sb => sb.ZeroRated);
            var totalDiscount = salesBook.Sum(sb => sb.Discount);
            var totalNetSales = salesBook.Sum(sb => sb.NetSales);

            // Create the Excel package
            using var package = new ExcelPackage();
            // Add a new worksheet to the Excel package
            var worksheet = package.Workbook.Worksheets.Add("SalesBook");

            // Set the column headers
            var mergedCells = worksheet.Cells["A1:C1"];
            mergedCells.Merge = true;
            mergedCells.Value = "SALES BOOK";
            mergedCells.Style.Font.Size = 13;

            worksheet.Cells["A2"].Value = "Date Range:";
            worksheet.Cells["A3"].Value = "Extracted By:";

            worksheet.Cells["B2"].Value = $"{dateFrom} - {dateTo}";
            worksheet.Cells["B3"].Value = $"{extractedBy}";

            worksheet.Cells["A7"].Value = "Tran. Date";
            worksheet.Cells["B7"].Value = "Serial Number";
            worksheet.Cells["C7"].Value = "Customer Name";
            worksheet.Cells["D7"].Value = "Tin#";
            worksheet.Cells["E7"].Value = "Address";
            worksheet.Cells["F7"].Value = "Description";
            worksheet.Cells["G7"].Value = "Amount";
            worksheet.Cells["H7"].Value = "Vat Amount";
            worksheet.Cells["I7"].Value = "Vatable Sales";
            worksheet.Cells["J7"].Value = "Vat-Exempt Sales";
            worksheet.Cells["K7"].Value = "Zero-Rated Sales";
            worksheet.Cells["L7"].Value = "Discount";
            worksheet.Cells["M7"].Value = "Net Sales";

            // Apply styling to the header row
            using (var range = worksheet.Cells["A7:M7"])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.LightGray);
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            }

            // Populate the data rows
            int row = 8;
            string currencyFormat = "#,##0.0000";

            foreach (var cv in salesBook)
            {
                worksheet.Cells[row, 1].Value = cv.TransactionDate.ToString(CS.BIR_Date_Format);;
                worksheet.Cells[row, 2].Value = cv.SerialNo;
                worksheet.Cells[row, 3].Value = cv.SoldTo;
                worksheet.Cells[row, 4].Value = cv.TinNo;
                worksheet.Cells[row, 5].Value = cv.Address;
                worksheet.Cells[row, 6].Value = cv.Description;
                worksheet.Cells[row, 7].Value = cv.Amount;
                worksheet.Cells[row, 8].Value = cv.VatAmount;
                worksheet.Cells[row, 9].Value = cv.VatableSales;
                worksheet.Cells[row, 10].Value = cv.VatExemptSales;
                worksheet.Cells[row, 11].Value = cv.ZeroRated;
                worksheet.Cells[row, 12].Value = cv.Discount;
                worksheet.Cells[row, 13].Value = cv.NetSales;

                worksheet.Cells[row, 7].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 11].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 12].Style.Numberformat.Format = currencyFormat;
                worksheet.Cells[row, 13].Style.Numberformat.Format = currencyFormat;

                row++;
            }

            worksheet.Cells[row, 6].Value = "Total ";
            worksheet.Cells[row, 7].Value = totalAmount;
            worksheet.Cells[row, 8].Value = totalVatAmount;
            worksheet.Cells[row, 9].Value = totalVatableSales;
            worksheet.Cells[row, 10].Value = totalVatExemptSales;
            worksheet.Cells[row, 11].Value = totalZeroRatedSales;
            worksheet.Cells[row, 12].Value = totalDiscount;
            worksheet.Cells[row, 13].Value = totalNetSales;

            worksheet.Cells[row, 7].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 8].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 9].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 10].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 11].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 12].Style.Numberformat.Format = currencyFormat;
            worksheet.Cells[row, 13].Style.Numberformat.Format = currencyFormat;

            // Apply style to subtotal row
            using (var range = worksheet.Cells[row, 1, row, 13])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(172, 185, 202));
            }

            using (var range = worksheet.Cells[row, 6, row, 13])
            {
                range.Style.Font.Bold = true;
                range.Style.Border.Top.Style = ExcelBorderStyle.Thin; // Single top border
                range.Style.Border.Bottom.Style = ExcelBorderStyle.Double; // Double bottom border
            }

            // Auto-fit columns for better readability
            worksheet.Cells.AutoFitColumns();
            worksheet.View.FreezePanes(8, 1);

            // Convert the Excel package to a byte array
            var excelBytes = package.GetAsByteArray();

            return File(excelBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "SalesBook.xlsx");
        }

        #endregion -- Generate SalesBook .Csv File --
    }
}
