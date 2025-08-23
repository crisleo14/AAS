using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Accounting_System.Models.Reports;
using Accounting_System.Models.ViewModels;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Linq.Dynamic.Core;
using Microsoft.IdentityModel.Tokens;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class JournalVoucherController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly JournalVoucherRepo _journalVoucherRepo;

        private readonly CheckVoucherRepo _checkVoucherRepo;

        private readonly ReceivingReportRepo _receivingReportRepo;

        private readonly PurchaseOrderRepo _purchaseOrderRepo;

        private readonly GeneralRepo _generalRepo;

        public JournalVoucherController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, JournalVoucherRepo journalVoucherRepo, GeneralRepo generalRepo, CheckVoucherRepo checkVoucherRepo, ReceivingReportRepo receivingReportRepo, PurchaseOrderRepo purchaseOrderRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _journalVoucherRepo = journalVoucherRepo;
            _generalRepo = generalRepo;
            _checkVoucherRepo = checkVoucherRepo;
            _receivingReportRepo = receivingReportRepo;
            _purchaseOrderRepo = purchaseOrderRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            if (view == nameof(DynamicView.JournalVoucher))
            {
                var journalVouchers = await _journalVoucherRepo.GetJournalVouchersAsync(cancellationToken);

                return View("ImportExportIndex", journalVouchers);
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetJournalVouchers([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var journalVouchers = await _journalVoucherRepo.GetJournalVouchersAsync(cancellationToken);
                // Search filter
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();
                    journalVouchers = journalVouchers
                        .Where(jv =>
                            jv.JournalVoucherHeaderNo!.ToLower().Contains(searchValue) ||
                            jv.Date.ToString("MMM dd, yyyy").ToLower().Contains(searchValue) ||
                            jv.References?.ToLower().Contains(searchValue) == true ||
                            jv.Particulars.ToLower().Contains(searchValue) ||
                            jv.CRNo?.ToLower().Contains(searchValue) == true ||
                            jv.JVReason.ToLower().Contains(searchValue) ||
                            jv.CheckVoucherHeader?.CheckVoucherHeaderNo?.ToLower().Contains(searchValue) == true ||
                            jv.CreatedBy!.ToLower().Contains(searchValue)
                            )
                        .ToList();
                }
                // Sorting
                if (parameters.Order != null && parameters.Order.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";
                    journalVouchers = journalVouchers
                        .AsQueryable()
                        .OrderBy($"{columnName} {sortDirection}")
                        .ToList();
                }
                var totalRecords = journalVouchers.Count();
                var pagedData = journalVouchers
                    .Skip(parameters.Start)
                    .Take(parameters.Length)
                    .ToList();
                return Json(new
                {
                    draw = parameters.Draw,
                    recordsTotal = totalRecords,
                    recordsFiltered = totalRecords,
                    data = pagedData
                });
            }
            catch (Exception ex)
            {
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllJournalVoucherIds(CancellationToken cancellationToken)
        {
            var journalVoucherIds = await _dbContext.JournalVoucherHeaders
                                     .Select(jv => jv.JournalVoucherHeaderId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(journalVoucherIds);
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var viewModel = new JournalVoucherVM
            {
                Header = new JournalVoucherHeader(),
                Details = new List<JournalVoucherDetail>()
            };

            viewModel.Header.COA = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);
            viewModel.Header.CheckVoucherHeaders = await _dbContext.CheckVoucherHeaders
                .Where(cvh => cvh.IsPosted)
                .OrderBy(c => c.CheckVoucherHeaderId)
                .Select(cvh => new SelectListItem
                {
                    Value = cvh.CheckVoucherHeaderId.ToString(),
                    Text = cvh.CheckVoucherHeaderNo
                })
                .ToListAsync(cancellationToken);

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> Create(JournalVoucherVM? model, string[] accountNumber, decimal[]? debit, decimal[]? credit, CancellationToken cancellationToken)
        {
            model!.Header!.COA = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            model.Header.CheckVoucherHeaders = await _dbContext.CheckVoucherHeaders
                .OrderBy(c => c.CheckVoucherHeaderId)
                .Select(cvh => new SelectListItem
                {
                    Value = cvh.CheckVoucherHeaderId.ToString(),
                    Text = cvh.CheckVoucherHeaderNo
                })
                .ToListAsync(cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Validating series

                    var generateJvNo = await _journalVoucherRepo.GenerateJVNo(cancellationToken);
                    var getLastNumber = long.Parse(generateJvNo.Substring(2));

                    if (getLastNumber > 9999999999)
                    {
                        TempData["error"] = "You reached the maximum Series Number";
                        return View(model);
                    }

                    var totalRemainingSeries = 9999999999 - getLastNumber;
                    if (getLastNumber >= 9999999899)
                    {
                        TempData["warning"] = $"Check Voucher created successfully, Warning {totalRemainingSeries} series numbers remaining";
                    }
                    else
                    {
                        TempData["success"] = "Check Voucher created successfully";
                    }

                    #endregion --Validating series

                    #region --Saving the default entries

                    //JV Header Entry
                    model.Header.JournalVoucherHeaderNo = generateJvNo;
                    model.Header.CreatedBy = _userManager.GetUserName(this.User);

                    await _dbContext.AddAsync(model.Header, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion --Saving the default entries

                    #region --CV Details Entry

                    var cvDetails = new List<JournalVoucherDetail>();

                    var totalDebit = 0m;
                    var totalCredit = 0m;
                    for (int i = 0; i < accountNumber.Length; i++)
                    {
                        var currentAccountNumber = accountNumber[i];
                        var accountTitle = await _dbContext.ChartOfAccounts
                            .FirstOrDefaultAsync(coa => coa.AccountNumber == currentAccountNumber, cancellationToken);
                        var currentDebit = debit![i];
                        var currentCredit = credit![i];
                        totalDebit += debit[i];
                        totalCredit += credit[i];

                        cvDetails.Add(
                            new JournalVoucherDetail
                            {
                                AccountNo = currentAccountNumber,
                                AccountName = accountTitle!.AccountName,
                                TransactionNo = generateJvNo,
                                Debit = currentDebit,
                                Credit = currentCredit,
                                JournalVoucherHeaderId = model.Header.JournalVoucherHeaderId
                            }
                        );
                    }
                    if (totalDebit != totalCredit)
                    {
                        TempData["error"] = "The debit and credit should be equal!";
                        return View(model);
                    }

                    await _dbContext.JournalVoucherDetails.AddRangeAsync(cvDetails, cancellationToken);

                    #endregion --CV Details Entry

                    #region --Audit Trail Recording

                    if (model.Header.OriginalSeriesNumber.IsNullOrEmpty() && model.Header.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.Header.CreatedBy!, $"Create new journal voucher# {model.Header.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(Index));
                }
            }

            TempData["error"] = "The information you submitted is not valid!";
            return View(model);
        }

        public async Task<IActionResult> GetCV(int id, CancellationToken cancellationToken)
        {
            var model = await _dbContext.CheckVoucherHeaders
                .Include(s => s.Supplier)
                .Include(cvd => cvd.Details)
                .FirstOrDefaultAsync(cvh => cvh.CheckVoucherHeaderId == id, cancellationToken);

            if (model != null)
            {
                return Json(new
                {
                    CVNo = model.CheckVoucherHeaderNo,
                    model.Date,
                    Name = model.Supplier!.SupplierName,
                    Address = model.Supplier.SupplierAddress,
                    TinNo = model.Supplier.SupplierTin,
                    model.PONo,
                    model.SINo,
                    model.Payee,
                    Amount = model.Total,
                    model.Particulars,
                    model.CheckNo,
                    AccountNo = model.Details.Select(jvd => jvd.AccountNo),
                    AccountName = model.Details.Select(jvd => jvd.AccountName),
                    Debit = model.Details.Select(jvd => jvd.Debit),
                    Credit = model.Details.Select(jvd => jvd.Credit),
                    TotalDebit = model.Details.Select(cvd => cvd.Debit).Sum(),
                    TotalCredit = model.Details.Select(cvd => cvd.Credit).Sum(),
                });
            }

            return Json(null);
        }

        [HttpGet]
        public async Task<IActionResult> Print(int? id, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                return NotFound();
            }

            var header = await _dbContext.JournalVoucherHeaders
                .Include(cv => cv.CheckVoucherHeader)
                .ThenInclude(supplier => supplier!.Supplier)
                .FirstOrDefaultAsync(jvh => jvh.JournalVoucherHeaderId == id.Value, cancellationToken);

            if (header == null)
            {
                return NotFound();
            }

            var details = await _dbContext.JournalVoucherDetails
                .Where(jvd => jvd.TransactionNo == header.JournalVoucherHeaderNo)
                .ToListAsync(cancellationToken);

            //if (header.Category == "Trade")
            //{
            //    var siArray = new string[header.RRNo.Length];
            //    for (int i = 0; i < header.RRNo.Length; i++)
            //    {
            //        var rrValue = header.RRNo[i];

            //        var rr = await _dbContext.ReceivingReports
            //                    .FirstOrDefaultAsync(p => p.RRNo == rrValue);

            //        siArray[i] = rr.SupplierInvoiceNumber;
            //    }

            //    ViewBag.SINoArray = siArray;
            //}

            var viewModel = new JournalVoucherVM
            {
                Header = header,
                Details = details
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Printed(int id, CancellationToken cancellationToken)
        {
            var jv = await _dbContext.JournalVoucherHeaders.FirstOrDefaultAsync(x => x.JournalVoucherHeaderId == id, cancellationToken);
            if (jv != null && !jv.IsPrinted)
            {

                #region --Audit Trail Recording

                if (jv.OriginalSeriesNumber.IsNullOrEmpty() && jv.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var printedBy = _userManager.GetUserName(this.User);
                    AuditTrail auditTrailBook = new(printedBy!, $"Printed original copy of jv# {jv.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                jv.IsPrinted = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(Print), new { id });
        }

        public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
        {
            var modelHeader = await _dbContext.JournalVoucherHeaders.FirstOrDefaultAsync(x => x.JournalVoucherHeaderId == id, cancellationToken);

            if (modelHeader != null)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var modelDetails = await _dbContext.JournalVoucherDetails.Where(jvd => jvd.TransactionNo == modelHeader.JournalVoucherHeaderNo).ToListAsync(cancellationToken);
                    if (!modelHeader.IsPosted)
                    {
                        modelHeader.IsPosted = true;
                        modelHeader.PostedBy = _userManager.GetUserName(this.User);
                        modelHeader.PostedDate = DateTime.Now;

                        #region --General Ledger Book Recording(GL)--

                        var accountTitlesDto = await _generalRepo.GetListOfAccountTitleDto(cancellationToken);
                        var ledgers = new List<GeneralLedgerBook>();
                        foreach (var details in modelDetails)
                        {
                            var account = accountTitlesDto.Find(c => c.AccountNumber == details.AccountNo) ?? throw new ArgumentException($"Account number '{details.AccountNo}', Account title '{details.AccountName}' not found.");
                            ledgers.Add(
                                    new GeneralLedgerBook
                                    {
                                        Date = modelHeader.Date,
                                        Reference = modelHeader.JournalVoucherHeaderNo!,
                                        Description = modelHeader.Particulars,
                                        AccountNo = account.AccountNumber,
                                        AccountTitle = account.AccountName,
                                        Debit = details.Debit,
                                        Credit = details.Credit,
                                        CreatedBy = modelHeader.CreatedBy,
                                        CreatedDate = modelHeader.CreatedDate
                                    }
                                );
                        }

                        if (!_generalRepo.IsJournalEntriesBalanced(ledgers))
                        {
                            throw new ArgumentException("Debit and Credit is not equal, check your entries.");
                        }

                        await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledgers, cancellationToken);

                        #endregion --General Ledger Book Recording(GL)--

                        #region --Journal Book Recording(JV)--

                        var journalBook = new List<JournalBook>();
                        foreach (var details in modelDetails)
                        {
                            journalBook.Add(
                                    new JournalBook
                                    {
                                        Date = modelHeader.Date,
                                        Reference = modelHeader.JournalVoucherHeaderNo!,
                                        Description = modelHeader.Particulars,
                                        AccountTitle = details.AccountNo + " " + details.AccountName,
                                        Debit = details.Debit,
                                        Credit = details.Credit,
                                        CreatedBy = modelHeader.CreatedBy,
                                        CreatedDate = modelHeader.CreatedDate
                                    }
                                );
                        }

                        await _dbContext.JournalBooks.AddRangeAsync(journalBook, cancellationToken);

                        #endregion --Journal Book Recording(JV)--

                        #region --Audit Trail Recording

                        if (modelHeader.OriginalSeriesNumber.IsNullOrEmpty() && modelHeader.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(modelHeader.PostedBy!, $"Posted journal voucher# {modelHeader.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Journal Voucher has been Posted.";
                    }
                    return RedirectToAction(nameof(Print), new { id });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Print), new { id });
                }
            }

            return NotFound();
        }

        public async Task<IActionResult> Void(int id, CancellationToken cancellationToken)
        {
            var model = await _dbContext.JournalVoucherHeaders.FirstOrDefaultAsync(x => x.JournalVoucherHeaderId == id, cancellationToken);
            var findJournalVoucherInJournalBook = await _dbContext.JournalBooks.Where(jb => jb.Reference == model!.JournalVoucherHeaderNo).ToListAsync(cancellationToken);
            var findJournalVoucherInGeneralLedger = await _dbContext.GeneralLedgerBooks.Where(jb => jb.Reference == model!.JournalVoucherHeaderNo).ToListAsync(cancellationToken);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                if (model != null)
                {
                    if (!model.IsVoided)
                    {
                        if (model.IsPosted)
                        {
                            model.IsPosted = false;
                        }

                        model.IsVoided = true;
                        model.VoidedBy = _userManager.GetUserName(this.User);
                        model.VoidedDate = DateTime.Now;

                        if (findJournalVoucherInJournalBook.Any())
                        {
                            await _generalRepo.RemoveRecords<JournalBook>(crb => crb.Reference == model.JournalVoucherHeaderNo, cancellationToken);
                        }
                        if (findJournalVoucherInGeneralLedger.Any())
                        {
                            await _generalRepo.RemoveRecords<GeneralLedgerBook>(gl => gl.Reference == model.JournalVoucherHeaderNo, cancellationToken);
                        }

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.VoidedBy!, $"Voided journal voucher# {model.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Journal Voucher has been Voided.";
                    }
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            return NotFound();
        }

        public async Task<IActionResult> Cancel(int id, string cancellationRemarks, CancellationToken cancellationToken)
        {
            var model = await _dbContext.JournalVoucherHeaders.FirstOrDefaultAsync(x => x.JournalVoucherHeaderId == id, cancellationToken);
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                if (model != null)
                {
                    if (!model.IsCanceled)
                    {
                        model.IsCanceled = true;
                        model.CanceledBy = _userManager.GetUserName(this.User);
                        model.CanceledDate = DateTime.Now;
                        model.CancellationRemarks = cancellationRemarks;

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.CanceledBy!, $"Cancelled journal voucher# {model.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Journal Voucher has been Cancelled.";
                    }
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            return NotFound();
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            if (id == 0)
            {
                return NotFound();
            }
            var existingHeaderModel = await _dbContext.JournalVoucherHeaders
                .Include(jv => jv.CheckVoucherHeader)
                .FirstOrDefaultAsync(cvh => cvh.JournalVoucherHeaderId == id, cancellationToken);
            var existingDetailsModel = await _dbContext.JournalVoucherDetails
                .Where(cvd => cvd.TransactionNo == existingHeaderModel!.JournalVoucherHeaderNo)
                .ToListAsync(cancellationToken);

            if (existingHeaderModel == null || !existingDetailsModel.Any())
            {
                return NotFound();
            }

            var accountNumbers = existingDetailsModel.Select(model => model.AccountNo).ToArray();
            var accountTitles = existingDetailsModel.Select(model => model.AccountName).ToArray();
            var debit = existingDetailsModel.Select(model => model.Debit).ToArray();
            var credit = existingDetailsModel.Select(model => model.Credit).ToArray();

            JournalVoucherViewModel model = new()
            {
                JVId = existingHeaderModel.JournalVoucherHeaderId,
                JVNo = existingHeaderModel.JournalVoucherHeaderNo,
                TransactionDate = existingHeaderModel.Date,
                References = existingHeaderModel.References,
                CVId = existingHeaderModel.CVId,
                Particulars = existingHeaderModel.Particulars,
                CRNo = existingHeaderModel.CRNo,
                JVReason = existingHeaderModel.JVReason,
                AccountNumber = accountNumbers,
                AccountTitle = accountTitles,
                Debit = debit,
                Credit = credit,
                CheckVoucherHeaders = await _dbContext.CheckVoucherHeaders
                .OrderBy(c => c.CheckVoucherHeaderId)
                .Select(cvh => new SelectListItem
                {
                    Value = cvh.CheckVoucherHeaderId.ToString(),
                    Text = cvh.CheckVoucherHeaderNo
                })
                .ToListAsync(cancellationToken),
                COA = await _dbContext.ChartOfAccounts
                    .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber!.Contains(excludedNumber)) && !coa.HasChildren)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(JournalVoucherViewModel viewModel, IFormFile? file, CancellationToken cancellationToken)
        {
            var existingModel = await _dbContext.JournalVoucherHeaders
                .Include(jvd => jvd.Details)
                .FirstOrDefaultAsync(jvh => jvh.JournalVoucherHeaderId == viewModel.JVId, cancellationToken);
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Saving the default entries

                    existingModel!.JournalVoucherHeaderNo = viewModel.JVNo;
                    existingModel.Date = viewModel.TransactionDate;
                    existingModel.References = viewModel.References;
                    existingModel.CVId = viewModel.CVId;
                    existingModel.Particulars = viewModel.Particulars;
                    existingModel.CRNo = viewModel.CRNo;
                    existingModel.JVReason = viewModel.JVReason;

                    #endregion --Saving the default entries

                    #region --CV Details Entry

                    // Dictionary to keep track of AccountNo and their ids for comparison
                    var accountTitleDict = new Dictionary<string, List<int>>();
                    foreach (var details in existingModel.Details)
                    {
                        if (!accountTitleDict.ContainsKey(details.AccountNo))
                        {
                            accountTitleDict[details.AccountNo] = new List<int>();
                        }
                        accountTitleDict[details.AccountNo].Add(details.JournalVoucherDetailId);
                    }

                    // Add or update records
                    for (int i = 0; i < viewModel.AccountTitle?.Length; i++)
                    {

                        if (accountTitleDict.TryGetValue(viewModel.AccountNumber?[i]!, out var ids))
                        {
                            // Update the first matching record and remove it from the list
                            var detailsId = ids.First();
                            ids.RemoveAt(0);
                            var details = existingModel.Details.First(o => o.JournalVoucherDetailId == detailsId);

                            var acctNo = await _dbContext.ChartOfAccounts
                                .FirstOrDefaultAsync(x => x.AccountName == viewModel.AccountTitle[i], cancellationToken: cancellationToken);

                            details.AccountNo = acctNo?.AccountNumber ?? "";
                            details.AccountName = viewModel.AccountTitle[i];
                            details.Debit = viewModel.Debit[i];
                            details.Credit = viewModel.Credit[i];
                            details.TransactionNo = existingModel.JournalVoucherHeaderNo!;
                            details.JournalVoucherHeaderId = existingModel.JournalVoucherHeaderId;

                            if (ids.Count == 0)
                            {
                                accountTitleDict.Remove(viewModel.AccountNumber![i]);
                            }
                        }
                        else
                        {
                            // Add new record
                            var newDetails = new JournalVoucherDetail
                            {
                                AccountNo = viewModel.AccountNumber![i],
                                AccountName = viewModel.AccountTitle[i],
                                Debit = viewModel.Debit[i],
                                Credit = viewModel.Credit[i],
                                TransactionNo = existingModel.JournalVoucherHeaderNo!,
                                JournalVoucherHeaderId = existingModel.JournalVoucherHeaderId
                            };
                            await _dbContext.JournalVoucherDetails.AddAsync(newDetails, cancellationToken);
                        }
                    }

                    // Remove remaining records that were duplicates
                    foreach (var ids in accountTitleDict.Values)
                    {
                        foreach (var id in ids)
                        {
                            var details = existingModel.Details.First(o => o.JournalVoucherDetailId == id);
                            _dbContext.JournalVoucherDetails.Remove(details);
                        }
                    }

                    #endregion --CV Details Entry

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (existingModel.OriginalSeriesNumber.IsNullOrEmpty() && existingModel.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!, $"Edit journal voucher# {viewModel.JVNo}", "Journal Voucher", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);  // await the SaveChangesAsync method
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Journal Voucher edited successfully";
                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    viewModel.CheckVoucherHeaders = await _dbContext.CheckVoucherHeaders
                        .OrderBy(c => c.CheckVoucherHeaderId)
                        .Select(cvh => new SelectListItem
                        {
                            Value = cvh.CheckVoucherHeaderId.ToString(),
                            Text = cvh.CheckVoucherHeaderNo
                        })
                        .ToListAsync(cancellationToken);
                    viewModel.COA = await _dbContext.ChartOfAccounts
                        .Where(coa =>
                            !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber =>
                                coa.AccountNumber!.Contains(excludedNumber)) && !coa.HasChildren)
                        .Select(s => new SelectListItem
                        {
                            Value = s.AccountNumber,
                            Text = s.AccountNumber + " " + s.AccountName
                        })
                        .ToListAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(viewModel);
                }
            }

            TempData["error"] = "The information provided was invalid.";
            viewModel.CheckVoucherHeaders = await _dbContext.CheckVoucherHeaders
                .OrderBy(c => c.CheckVoucherHeaderId)
                .Select(cvh => new SelectListItem
                {
                    Value = cvh.CheckVoucherHeaderId.ToString(),
                    Text = cvh.CheckVoucherHeaderNo
                })
                .ToListAsync(cancellationToken);
            viewModel.COA = await _dbContext.ChartOfAccounts
                .Where(coa =>
                    !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber =>
                        coa.AccountNumber!.Contains(excludedNumber)) && !coa.HasChildren)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);
            return View(viewModel);
        }

        //Upload as .xlsx file.(Import)
        #region -- import xlsx record --

        [HttpPost]
        public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return RedirectToAction(nameof(Index));
            }

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream, cancellationToken);
                stream.Position = 0;
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    using var package = new ExcelPackage(stream);
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "JournalVoucherHeader");

                    var worksheet2 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "JournalVoucherDetails");

                    var worksheet3 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "PurchaseOrder");

                    var worksheet4 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "ReceivingReport");

                    var worksheet5 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CheckVoucherHeader");

                    var worksheet6 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CheckVoucherDetails");

                    if (worksheet == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets of journal voucher header.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                    }
                    if (worksheet2 == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets of journal voucher details.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                    }
                    if (worksheet.ToString() != "JournalVoucherHeader")
                    {
                        TempData["error"] = "The Excel file is not related to journal voucher.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                    }

                    #region -- Purchase Order Import --

                    var poRowCount = worksheet3?.Dimension?.Rows ?? 0;
                    var poDictionary = new Dictionary<string, bool>();
                    var purchaseOrderList = await _dbContext
                        .PurchaseOrders
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= poRowCount; row++)  // Assuming the first row is the header
                    {
                        if (worksheet3 == null || poRowCount == 0)
                        {
                            continue;
                        }
                        var purchaseOrder = new PurchaseOrder
                        {
                            PurchaseOrderNo = worksheet3.Cells[row, 16].Text,
                            Date = DateOnly.TryParse(worksheet3.Cells[row, 1].Text, out DateOnly dueDate) ? dueDate : default,
                            Terms = worksheet3.Cells[row, 2].Text,
                            Quantity = decimal.TryParse(worksheet3.Cells[row, 3].Text, out decimal quantity) ? quantity : 0,
                            Price = decimal.TryParse(worksheet3.Cells[row, 4].Text, out decimal price) ? price : 0,
                            Amount = decimal.TryParse(worksheet3.Cells[row, 5].Text, out decimal amount) ? amount : 0,
                            FinalPrice = decimal.TryParse(worksheet3.Cells[row, 6].Text, out decimal finalPrice) ? finalPrice : 0,
                            // QuantityReceived = decimal.TryParse(worksheet.Cells[row, 7].Text, out decimal quantityReceived) ? quantityReceived : 0,
                            // IsReceived = bool.TryParse(worksheet.Cells[row, 8].Text, out bool isReceived) ? isReceived : default,
                            // ReceivedDate = DateTime.TryParse(worksheet.Cells[row, 9].Text, out DateTime receivedDate) ? receivedDate : default,
                            Remarks = worksheet3.Cells[row, 10].Text,
                            CreatedBy = worksheet3.Cells[row, 11].Text,
                            CreatedDate = DateTime.TryParse(worksheet3.Cells[row, 12].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet3.Cells[row, 19].Text,
                            PostedDate = DateTime.TryParse(worksheet3.Cells[row, 20].Text, out DateTime postedDate) ? postedDate : default,
                            IsClosed = bool.TryParse(worksheet3.Cells[row, 13].Text, out bool isClosed) && isClosed,
                            CancellationRemarks = worksheet3.Cells[row, 14].Text != "" ? worksheet3.Cells[row, 14].Text : null,
                            OriginalProductId = int.TryParse(worksheet3.Cells[row, 15].Text, out int originalProductId) ? originalProductId : 0,
                            OriginalSeriesNumber = worksheet3.Cells[row, 16].Text,
                            OriginalSupplierId = int.TryParse(worksheet3.Cells[row, 17].Text, out int originalSupplierId) ? originalSupplierId : 0,
                            OriginalDocumentId = int.TryParse(worksheet3.Cells[row, 18].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        if (!poDictionary.TryAdd(purchaseOrder.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        if (purchaseOrderList.Any(po => po.OriginalDocumentId == purchaseOrder.OriginalDocumentId))
                        {
                            var poChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingPurchaseOrder = await _dbContext.PurchaseOrders.FirstOrDefaultAsync(si => si.OriginalDocumentId == purchaseOrder.OriginalDocumentId, cancellationToken);

                            if (existingPurchaseOrder!.PurchaseOrderNo!.TrimStart().TrimEnd() != worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                poChanges["PONo"] = (existingPurchaseOrder.PurchaseOrderNo.TrimStart().TrimEnd(), worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Date"] = (existingPurchaseOrder.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.Terms.TrimStart().TrimEnd() != worksheet3.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Terms"] = (existingPurchaseOrder.Terms.TrimStart().TrimEnd(), worksheet3.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.Quantity.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Quantity"] = (existingPurchaseOrder.Quantity.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.Price.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Price"] = (existingPurchaseOrder.Price.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Amount"] = (existingPurchaseOrder.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.FinalPrice?.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["FinalPrice"] = (existingPurchaseOrder.FinalPrice?.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())!;
                            }

                            if (existingPurchaseOrder.Remarks.TrimStart().TrimEnd() != worksheet3.Cells[row, 10].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Remarks"] = (existingPurchaseOrder.Remarks.TrimStart().TrimEnd(), worksheet3.Cells[row, 10].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.CreatedBy!.TrimStart().TrimEnd() != worksheet3.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CreatedBy"] = (existingPurchaseOrder.CreatedBy.TrimStart().TrimEnd(), worksheet3.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet3.Cells[row, 12].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CreatedDate"] = (existingPurchaseOrder.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet3.Cells[row, 12].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.IsClosed.ToString().ToUpper().TrimStart().TrimEnd() != worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                poChanges["IsClosed"] = (existingPurchaseOrder.IsClosed.ToString().ToUpper().TrimStart().TrimEnd(), worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingPurchaseOrder.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingPurchaseOrder.CancellationRemarks.TrimStart().TrimEnd()) != worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CancellationRemarks"] = (existingPurchaseOrder.CancellationRemarks?.TrimStart().TrimEnd(), worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingPurchaseOrder.OriginalProductId.ToString()!.TrimStart().TrimEnd() != (worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["OriginalProductId"] = (existingPurchaseOrder.OriginalProductId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                poChanges["OriginalSeriesNumber"] = (existingPurchaseOrder.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.OriginalSupplierId.ToString()!.TrimStart().TrimEnd() != (worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["SupplierId"] = (existingPurchaseOrder.SupplierId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if (existingPurchaseOrder.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["OriginalDocumentId"] = (existingPurchaseOrder.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd());
                            }

                            if (poChanges.Any())
                            {
                                await _purchaseOrderRepo.LogChangesAsync(existingPurchaseOrder.OriginalDocumentId, poChanges, _userManager.GetUserName(this.User), existingPurchaseOrder.PurchaseOrderNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!purchaseOrder.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(purchaseOrder.CreatedBy, $"Create new purchase order# {purchaseOrder.PurchaseOrderNo}", "Purchase Order", ipAddress!, purchaseOrder.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!purchaseOrder.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(purchaseOrder.PostedBy, $"Posted purchase order# {purchaseOrder.PurchaseOrderNo}", "Purchase Order", ipAddress!, purchaseOrder.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        var getProduct = await _dbContext.Products
                            .Where(p => p.OriginalProductId == purchaseOrder.OriginalProductId)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (getProduct != null)
                        {
                            purchaseOrder.ProductId = getProduct.ProductId;

                            purchaseOrder.ProductNo = getProduct.ProductCode;
                        }
                        else
                        {
                            throw new InvalidOperationException("Please upload the Excel file for the product master file first.");
                        }

                        var getSupplier = await _dbContext.Suppliers
                            .Where(c => c.OriginalSupplierId == purchaseOrder.OriginalSupplierId)
                            .FirstOrDefaultAsync(cancellationToken);

                        if (getSupplier != null)
                        {
                            purchaseOrder.SupplierId = getSupplier.SupplierId;

                            purchaseOrder.SupplierNo = getSupplier.Number;
                        }
                        else
                        {
                            throw new InvalidOperationException("Please upload the Excel file for the supplier master file first.");
                        }

                        await _dbContext.PurchaseOrders.AddAsync(purchaseOrder, cancellationToken);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Purchase Order Import --

                    #region -- Receiving Report Import --

                    var rrRowCount = worksheet4?.Dimension?.Rows ?? 0;
                    var rrDictionary = new Dictionary<string, bool>();
                    var receivingReportList = await _dbContext
                        .ReceivingReports
                        .ToListAsync(cancellationToken);
                    for (int row = 2; row <= rrRowCount; row++)  // Assuming the first row is the header
                    {
                        if (worksheet4 == null || rrRowCount == 0)
                        {
                            continue;
                        }
                        var receivingReport = new ReceivingReport
                        {
                            ReceivingReportNo = worksheet4.Cells[row, 21].Text,
                            Date = DateOnly.TryParse(worksheet4.Cells[row, 1].Text, out DateOnly date) ? date : default,
                            DueDate = DateOnly.TryParse(worksheet4.Cells[row, 2].Text, out DateOnly dueDate) ? dueDate : default,
                            SupplierInvoiceNumber = worksheet4.Cells[row, 3].Text != "" ? worksheet4.Cells[row, 3].Text : null,
                            SupplierInvoiceDate = worksheet4.Cells[row, 4].Text,
                            TruckOrVessels = worksheet4.Cells[row, 5].Text,
                            QuantityDelivered = decimal.TryParse(worksheet4.Cells[row, 6].Text, out decimal quantityDelivered) ? quantityDelivered : 0,
                            QuantityReceived = decimal.TryParse(worksheet4.Cells[row, 7].Text, out decimal quantityReceived) ? quantityReceived : 0,
                            GainOrLoss = decimal.TryParse(worksheet4.Cells[row, 8].Text, out decimal gainOrLoss) ? gainOrLoss : 0,
                            Amount = decimal.TryParse(worksheet4.Cells[row, 9].Text, out decimal amount) ? amount : 0,
                            OtherRef = worksheet4.Cells[row, 10].Text != "" ? worksheet4.Cells[row, 10].Text : null,
                            Remarks = worksheet4.Cells[row, 11].Text,
                            // AmountPaid = decimal.TryParse(worksheet.Cells[row, 12].Text, out decimal amountPaid) ? amountPaid : 0,
                            // IsPaid = bool.TryParse(worksheet.Cells[row, 13].Text, out bool IsPaid) ? IsPaid : default,
                            // PaidDate = DateTime.TryParse(worksheet.Cells[row, 14].Text, out DateTime paidDate) ? paidDate : DateTime.MinValue,
                            // CanceledQuantity = decimal.TryParse(worksheet.Cells[row, 15].Text, out decimal netAmountOfEWT) ? netAmountOfEWT : 0,
                            CreatedBy = worksheet4.Cells[row, 16].Text,
                            CreatedDate = DateTime.TryParse(worksheet4.Cells[row, 17].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet4.Cells[row, 23].Text,
                            PostedDate = DateTime.TryParse(worksheet4.Cells[row, 24].Text, out DateTime postedDate) ? postedDate : default,
                            CancellationRemarks = worksheet4.Cells[row, 18].Text != "" ? worksheet4.Cells[row, 18].Text : null,
                            ReceivedDate = DateOnly.TryParse(worksheet4.Cells[row, 19].Text, out DateOnly receivedDate) ? receivedDate : default,
                            OriginalPOId = int.TryParse(worksheet4.Cells[row, 20].Text, out int originalPoId) ? originalPoId : 0,
                            OriginalSeriesNumber = worksheet4.Cells[row, 21].Text,
                            OriginalDocumentId = int.TryParse(worksheet4.Cells[row, 22].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        if (!rrDictionary.TryAdd(receivingReport.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        //Checking for duplicate record
                        if (receivingReportList.Any(rr => rr.OriginalDocumentId == receivingReport.OriginalDocumentId))
                        {
                            var rrChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingReceivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(rr => rr.OriginalDocumentId == receivingReport.OriginalDocumentId, cancellationToken);

                            if (existingReceivingReport!.ReceivingReportNo!.TrimStart().TrimEnd() != worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["RRNo"] = (existingReceivingReport.ReceivingReportNo.TrimStart().TrimEnd(), worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["Date"] = (existingReceivingReport.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["DueDate"] = (existingReceivingReport.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.SupplierInvoiceNumber!.TrimStart().TrimEnd() != (worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["SupplierInvoiceNumber"] = (existingReceivingReport.SupplierInvoiceNumber.TrimStart().TrimEnd(), worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingReceivingReport.SupplierInvoiceDate!.TrimStart().TrimEnd() != worksheet4.Cells[row, 4].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["SupplierInvoiceDate"] = (existingReceivingReport.SupplierInvoiceDate.TrimStart().TrimEnd(), worksheet4.Cells[row, 4].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.TruckOrVessels.TrimStart().TrimEnd() != worksheet4.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["TruckOrVessels"] = (existingReceivingReport.TruckOrVessels.TrimStart().TrimEnd(), worksheet4.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.QuantityDelivered.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["QuantityDelivered"] = (existingReceivingReport.QuantityDelivered.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.QuantityReceived.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["QuantityReceived"] = (existingReceivingReport.QuantityReceived.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.GainOrLoss.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["GainOrLoss"] = (existingReceivingReport.GainOrLoss.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["Amount"] = (existingReceivingReport.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.OtherRef!.TrimStart().TrimEnd() != (worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OtherRef"] = (existingReceivingReport.OtherRef.TrimStart().TrimEnd(), worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingReceivingReport.Remarks.TrimStart().TrimEnd() != worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["Remarks"] = (existingReceivingReport.Remarks.TrimStart().TrimEnd(), worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.CreatedBy!.TrimStart().TrimEnd() != worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CreatedBy"] = (existingReceivingReport.CreatedBy.TrimStart().TrimEnd(), worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CreatedDate"] = (existingReceivingReport.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingReceivingReport.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingReceivingReport.CancellationRemarks.TrimStart().TrimEnd()) != worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CancellationRemarks"] = (existingReceivingReport.CancellationRemarks?.TrimStart().TrimEnd(), worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingReceivingReport.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["ReceivedDate"] = (existingReceivingReport.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingReceivingReport.OriginalPOId.ToString()!.TrimStart().TrimEnd() != (worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OriginalPOId"] = (existingReceivingReport.OriginalPOId.ToString()!.TrimStart().TrimEnd(), worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["OriginalSeriesNumber"] = (existingReceivingReport.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingReceivingReport.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OriginalDocumentId"] = (existingReceivingReport.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd());
                            }

                            if (rrChanges.Any())
                            {
                                await _receivingReportRepo.LogChangesAsync(existingReceivingReport.OriginalDocumentId, rrChanges, _userManager.GetUserName(this.User), existingReceivingReport.ReceivingReportNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!receivingReport.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(receivingReport.CreatedBy, $"Create new receiving report# {receivingReport.ReceivingReportNo}", "Receiving Report", ipAddress!, receivingReport.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!receivingReport.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(receivingReport.PostedBy, $"Posted receiving report# {receivingReport.ReceivingReportNo}", "Receiving Report", ipAddress!, receivingReport.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        var getPo = await _dbContext
                            .PurchaseOrders
                            .Where(po => po.OriginalDocumentId == receivingReport.OriginalPOId)
                            .FirstOrDefaultAsync(cancellationToken);

                        receivingReport.POId = getPo!.PurchaseOrderId;
                        receivingReport.PONo = getPo.PurchaseOrderNo;

                        await _dbContext.ReceivingReports.AddAsync(receivingReport, cancellationToken);
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Receiving Report Import --

                    #region -- Check Voucher Header Import --

                    var cvhRowCount = worksheet5?.Dimension?.Rows ?? 0;
                    var cvDictionary = new Dictionary<string, bool>();
                    var checkVoucherHeadersList = await _dbContext
                        .CheckVoucherHeaders
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= cvhRowCount; row++) // Assuming the first row is the header
                    {
                        if (worksheet5 == null || cvhRowCount == 0)
                        {
                            continue;
                        }
                        var checkVoucherHeader = new CheckVoucherHeader
                        {
                            CheckVoucherHeaderNo = await _checkVoucherRepo.GenerateCVNo(cancellationToken),
                            Date = DateOnly.TryParse(worksheet5.Cells[row, 1].Text, out DateOnly date)
                                ? date
                                : default,
                            RRNo = worksheet5.Cells[row, 2].Text.Split(',').Select(rrNo => rrNo.Trim()).ToArray(),
                            SINo = worksheet5.Cells[row, 3].Text.Split(',').Select(siNo => siNo.Trim()).ToArray(),
                            PONo = worksheet5.Cells[row, 4].Text.Split(',').Select(poNo => poNo.Trim()).ToArray(),
                            Particulars = worksheet5.Cells[row, 5].Text,
                            CheckNo = worksheet5.Cells[row, 6].Text,
                            Category = worksheet5.Cells[row, 7].Text,
                            Payee = worksheet5.Cells[row, 8].Text,
                            CheckDate = DateOnly.TryParse(worksheet5.Cells[row, 9].Text, out DateOnly checkDate)
                                ? checkDate
                                : default,
                            StartDate = DateOnly.TryParse(worksheet5.Cells[row, 10].Text, out DateOnly startDate)
                                ? startDate
                                : default,
                            EndDate = DateOnly.TryParse(worksheet5.Cells[row, 11].Text, out DateOnly endDate)
                                ? endDate
                                : default,
                            NumberOfMonths = int.TryParse(worksheet5.Cells[row, 12].Text, out int numberOfMonths)
                                ? numberOfMonths
                                : 0,
                            NumberOfMonthsCreated =
                                int.TryParse(worksheet5.Cells[row, 13].Text, out int numberOfMonthsCreated)
                                    ? numberOfMonthsCreated
                                    : 0,
                            LastCreatedDate =
                                DateTime.TryParse(worksheet5.Cells[row, 14].Text, out DateTime lastCreatedDate)
                                    ? lastCreatedDate
                                    : default,
                            AmountPerMonth =
                                decimal.TryParse(worksheet5.Cells[row, 15].Text, out decimal amountPerMonth)
                                    ? amountPerMonth
                                    : 0,
                            IsComplete = bool.TryParse(worksheet5.Cells[row, 16].Text, out bool isComplete) && isComplete,
                            AccruedType = worksheet5.Cells[row, 17].Text,
                            Reference = worksheet5.Cells[row, 18].Text,
                            CreatedBy = worksheet5.Cells[row, 19].Text,
                            CreatedDate = DateTime.TryParse(worksheet5.Cells[row, 20].Text, out DateTime createdDate)
                                ? createdDate
                                : default,
                            PostedBy = worksheet5.Cells[row, 32].Text,
                            PostedDate = DateTime.TryParse(worksheet5.Cells[row, 33].Text, out DateTime postedDate)
                                ? postedDate
                                : default,

                            Total = decimal.TryParse(worksheet5.Cells[row, 21].Text, out decimal total) ? total : 0,
                            Amount = worksheet5.Cells[row, 22].Text.Split(' ').Select(arrayAmount =>
                                decimal.TryParse(arrayAmount.Trim(), out decimal amount) ? amount : 0).ToArray(),
                            CheckAmount = decimal.TryParse(worksheet5.Cells[row, 23].Text, out decimal checkAmount)
                                ? checkAmount
                                : 0,
                            CvType = worksheet5.Cells[row, 24].Text,
                            AmountPaid = decimal.TryParse(worksheet5.Cells[row, 25].Text, out decimal amountPaid)
                                ? amountPaid
                                : 0,
                            IsPaid = bool.TryParse(worksheet5.Cells[row, 26].Text, out bool isPaid) && isPaid,
                            CancellationRemarks = worksheet5.Cells[row, 27].Text,
                            OriginalBankId = int.TryParse(worksheet5.Cells[row, 28].Text, out int originalBankId)
                                ? originalBankId
                                : 0,
                            OriginalSeriesNumber = worksheet5.Cells[row, 29].Text,
                            OriginalSupplierId =
                                int.TryParse(worksheet5.Cells[row, 30].Text, out int originalSupplierId)
                                    ? originalSupplierId
                                    : 0,
                            OriginalDocumentId =
                                int.TryParse(worksheet5.Cells[row, 31].Text, out int originalDocumentId)
                                    ? originalDocumentId
                                    : 0,
                        };

                        var cvhList = checkVoucherHeadersList.Any(cv => cv.OriginalDocumentId == checkVoucherHeader.OriginalDocumentId);
                        var existingCv = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(si => si.OriginalDocumentId == checkVoucherHeader.OriginalDocumentId, cancellationToken);
                        cvDictionary.TryAdd((existingCv != null ? existingCv.CheckVoucherHeaderNo : checkVoucherHeader.CheckVoucherHeaderNo) ?? checkVoucherHeader.CheckVoucherHeaderNo, cvhList);

                        if (cvhList)
                        {
                            var cvChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();

                            if (existingCv!.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet5.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["Date"] = (existingCv.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet5.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            var rrNo = existingCv.RRNo != null
                                ? string.Join(", ", existingCv.RRNo.Select(si => si.ToString()))
                                : null;
                            if (rrNo != null && rrNo.TrimStart().TrimEnd() != worksheet5.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["RRNo"] = (string.Join(", ", existingCv.RRNo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet5.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            var siNo = existingCv.SINo != null
                                ? string.Join(", ", existingCv.SINo.Select(si => si.ToString()))
                                : null;
                            if (siNo != null && siNo.TrimStart().TrimEnd() != worksheet5.Cells[row, 3].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["SINo"] = (string.Join(", ", existingCv.SINo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet5.Cells[row, 3].Text.TrimStart().TrimEnd());
                            }

                            var poNo = existingCv.PONo != null
                                ? string.Join(", ", existingCv.PONo.Select(si => si.ToString()))
                                : null;
                            if (poNo != null && poNo.TrimStart().TrimEnd() != worksheet5.Cells[row, 4].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["PONo"] = (string.Join(", ", existingCv.PONo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet5.Cells[row, 4].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.Particulars!.TrimStart().TrimEnd() != worksheet5.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["Particulars"] = (existingCv.Particulars.TrimStart().TrimEnd(), worksheet5.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.CheckNo!.TrimStart().TrimEnd() != worksheet5.Cells[row, 6].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["CheckNo"] = (existingCv.CheckNo.TrimStart().TrimEnd(), worksheet5.Cells[row, 6].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.Category.TrimStart().TrimEnd() != worksheet5.Cells[row, 7].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["Category"] = (existingCv.Category.TrimStart().TrimEnd(), worksheet5.Cells[row, 7].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.Payee!.TrimStart().TrimEnd() != worksheet5.Cells[row, 8].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["Payee"] = (existingCv.Payee.TrimStart().TrimEnd(), worksheet5.Cells[row, 8].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.CheckDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet5.Cells[row, 9].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 9].Text.TrimStart().TrimEnd()))
                            {
                                cvChanges["CheckDate"] = (existingCv.CheckDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet5.Cells[row, 9].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 9].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingCv.StartDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet5.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 10].Text.TrimStart().TrimEnd()))
                            {
                                cvChanges["StartDate"] = (existingCv.StartDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet5.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 10].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingCv.EndDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet5.Cells[row, 11].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 11].Text.TrimStart().TrimEnd()))
                            {
                                cvChanges["EndDate"] = (existingCv.EndDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet5.Cells[row, 11].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 11].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingCv.NumberOfMonths.ToString().TrimStart().TrimEnd() != worksheet5.Cells[row, 12].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["NumberOfMonths"] = (existingCv.NumberOfMonths.ToString().TrimStart().TrimEnd(), worksheet5.Cells[row, 12].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.NumberOfMonthsCreated.ToString().TrimStart().TrimEnd() != worksheet5.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["NumberOfMonthsCreated"] = (existingCv.NumberOfMonthsCreated.ToString().TrimStart().TrimEnd(), worksheet5.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.LastCreatedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet5.Cells[row, 14].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 14].Text.TrimStart().TrimEnd()))
                            {
                                cvChanges["LastCreatedDate"] = (existingCv.LastCreatedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet5.Cells[row, 14].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet5.Cells[row, 14].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingCv.AmountPerMonth.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet5.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                cvChanges["AmountPerMonth"] = (existingCv.AmountPerMonth.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet5.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCv.IsComplete.ToString().ToUpper().TrimStart().TrimEnd() != worksheet5.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["IsComplete"] = (existingCv.IsComplete.ToString().ToUpper().TrimStart().TrimEnd(), worksheet5.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.AccruedType!.TrimStart().TrimEnd() != worksheet5.Cells[row, 17].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["AccruedType"] = (existingCv.AccruedType.TrimStart().TrimEnd(), worksheet5.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.CvType!.TrimStart().TrimEnd() == "Payment")
                            {
                                var getCvInvoicing = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cvh => existingCv.Reference == cvh.CheckVoucherHeaderNo, cancellationToken);
                                if (getCvInvoicing != null && getCvInvoicing.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet5.Cells[row, 18].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Reference"] = (getCvInvoicing.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet5.Cells[row, 18].Text.TrimStart().TrimEnd());
                                }
                            }

                            if (existingCv.CreatedBy!.TrimStart().TrimEnd() != worksheet5.Cells[row, 19].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["CreatedBy"] = (existingCv.CreatedBy.TrimStart().TrimEnd(), worksheet5.Cells[row, 19].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet5.Cells[row, 20].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["CreatedDate"] = (existingCv.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet5.Cells[row, 20].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.Total.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet5.Cells[row, 21].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                cvChanges["Total"] = (existingCv.Total.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet5.Cells[row, 21].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCv.Category.TrimStart().TrimEnd() == "Trade")
                            {
                                var amount = existingCv.Amount != null
                                    ? string.Join(" ", existingCv.Amount.Select(si => si.ToString("N4")))
                                    : null;
                                if (amount != null && amount.TrimStart().TrimEnd() != worksheet5.Cells[row, 22].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Amount"] = (string.Join(" ", existingCv.Amount!.Select(si => si.ToString("F4").TrimStart().TrimEnd())), worksheet5.Cells[row, 22].Text.TrimStart().TrimEnd());
                                }
                            }

                            if (existingCv.CheckAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet5.Cells[row, 23].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                cvChanges["CheckAmount"] = (existingCv.CheckAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet5.Cells[row, 23].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCv.CvType.TrimStart().TrimEnd() != worksheet5.Cells[row, 24].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["CvType"] = (existingCv.CvType.TrimStart().TrimEnd(), worksheet5.Cells[row, 24].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.AmountPaid.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet5.Cells[row, 25].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                cvChanges["AmountPaid"] = (existingCv.AmountPaid.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet5.Cells[row, 25].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCv.IsPaid.ToString().ToUpper().TrimStart().TrimEnd() != worksheet5.Cells[row, 26].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["IsPaid"] = (existingCv.IsPaid.ToString().ToUpper().TrimStart().TrimEnd(), worksheet5.Cells[row, 26].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingCv.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingCv.CancellationRemarks.TrimStart().TrimEnd()) != worksheet5.Cells[row, 27].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["CancellationRemarks"] = (existingCv.CancellationRemarks!.TrimStart().TrimEnd(), worksheet5.Cells[row, 27].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.OriginalBankId.ToString()!.TrimStart().TrimEnd() != (worksheet5.Cells[row, 28].Text.TrimStart().TrimEnd() != "" ? worksheet5.Cells[row, 28].Text.TrimStart().TrimEnd() : 0.ToString()))
                            {
                                cvChanges["OriginalBankId"] = (existingCv.OriginalBankId.ToString()!.TrimStart().TrimEnd(), worksheet5.Cells[row, 28].Text.TrimStart().TrimEnd() != "" ? worksheet5.Cells[row, 28].Text.TrimStart().TrimEnd() : 0.ToString());
                            }

                            if (existingCv.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet5.Cells[row, 29].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["OriginalSeriesNumber"] = (existingCv.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet5.Cells[row, 29].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.OriginalSupplierId.ToString()!.TrimStart().TrimEnd() != worksheet5.Cells[row, 30].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["OriginalSupplierId"] = (existingCv.OriginalSupplierId.ToString()!.TrimStart().TrimEnd(), worksheet5.Cells[row, 30].Text.TrimStart().TrimEnd());
                            }

                            if (existingCv.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet5.Cells[row, 31].Text.TrimStart().TrimEnd())
                            {
                                cvChanges["OriginalDocumentId"] = (existingCv.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet5.Cells[row, 31].Text.TrimStart().TrimEnd());
                            }

                            if (cvChanges.Any())
                            {
                                await _checkVoucherRepo.LogChangesAsync(existingCv.OriginalDocumentId, cvChanges, _userManager.GetUserName(this.User), existingCv.CheckVoucherHeaderNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!checkVoucherHeader.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(checkVoucherHeader.CreatedBy, $"Create new check vouchcer# {checkVoucherHeader.CheckVoucherHeaderNo}", $"Check Voucher {(checkVoucherHeader.CvType == "Invoicing" ? "Non Trade Invoice" : checkVoucherHeader.CvType == "Payment" ? "Non Trade Payment" : "Trade")}", ipAddress!, checkVoucherHeader.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!checkVoucherHeader.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(checkVoucherHeader.PostedBy, $"Posted check voucher# {checkVoucherHeader.CheckVoucherHeaderNo}", $"Check Voucher {(checkVoucherHeader.CvType == "Invoicing" ? "Non Trade Invoice" : checkVoucherHeader.CvType == "Payment" ? "Non Trade Payment" : "Trade")}", ipAddress!, checkVoucherHeader.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        checkVoucherHeader.SupplierId = await _dbContext.Suppliers
                                                            .Where(supp =>
                                                                supp.OriginalSupplierId ==
                                                                checkVoucherHeader.OriginalSupplierId)
                                                            .Select(supp => (int?)supp.SupplierId)
                                                            .FirstOrDefaultAsync(cancellationToken) ??
                                                        throw new InvalidOperationException(
                                                            "Please upload the Excel file for the supplier master file first.");

                        if (checkVoucherHeader.CvType != "Invoicing")
                        {
                            checkVoucherHeader.BankId = await _dbContext.BankAccounts
                                                            .Where(bank =>
                                                                bank.OriginalBankId ==
                                                                checkVoucherHeader.OriginalBankId)
                                                            .Select(bank => (int?)bank.BankAccountId)
                                                            .FirstOrDefaultAsync(cancellationToken) ??
                                                        throw new InvalidOperationException(
                                                            "Please upload the Excel file for the bank account master file first.");
                        }

                        await _dbContext.CheckVoucherHeaders.AddAsync(checkVoucherHeader, cancellationToken);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }

                    #endregion -- Check Voucher Header Import --

                    #region -- Check Voucher Details Import --

                    var cvdRowCount = worksheet6?.Dimension?.Rows ?? 0;

                    for (int cvdRow = 2; cvdRow <= cvdRowCount; cvdRow++)
                    {
                        var cvRow = cvdRow;
                        var checkVoucherDetails = new CheckVoucherDetail
                        {
                            AccountNo = worksheet6!.Cells[cvdRow, 1].Text,
                            AccountName = worksheet6.Cells[cvdRow, 2].Text,
                            Debit = decimal.TryParse(worksheet6.Cells[cvdRow, 4].Text, out decimal debit)
                                ? debit
                                : 0,
                            Credit = decimal.TryParse(worksheet6.Cells[cvdRow, 5].Text, out decimal credit)
                                ? credit
                                : 0,
                            OriginalDocumentId = int.Parse(worksheet6.Cells[cvdRow, 7].Text)
                        };

                        var cvHeader = await _dbContext.CheckVoucherHeaders
                            .Where(cvh => cvh.OriginalDocumentId.ToString() == worksheet6.Cells[cvRow, 6].Text)
                            .FirstOrDefaultAsync(cancellationToken);

                        checkVoucherDetails.CheckVoucherHeaderId = cvHeader!.CheckVoucherHeaderId;
                        checkVoucherDetails.TransactionNo = cvHeader.CheckVoucherHeaderNo!;

                        if (cvDictionary.TryGetValue(checkVoucherDetails.TransactionNo, out var value) && !value)
                        {
                            await _dbContext.CheckVoucherDetails.AddAsync(checkVoucherDetails, cancellationToken);
                        }

                        if (cvDictionary.TryGetValue(checkVoucherDetails.TransactionNo, out var boolean) && boolean)
                        {
                            var cvdChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingCheckVoucherDetails = await _dbContext.CheckVoucherDetails
                                .Include(cvd => cvd.CheckVoucherHeader)
                                .FirstOrDefaultAsync(cvd => cvd.OriginalDocumentId == checkVoucherDetails.OriginalDocumentId, cancellationToken);

                            if (existingCheckVoucherDetails != null)
                            {
                                if (existingCheckVoucherDetails.AccountNo.TrimStart().TrimEnd() != worksheet6.Cells[cvdRow, 1].Text.TrimStart().TrimEnd())
                                {
                                    cvdChanges["AccountNo"] = (existingCheckVoucherDetails.AccountNo.TrimStart().TrimEnd(), worksheet6.Cells[cvdRow, 1].Text.TrimStart().TrimEnd());
                                }

                                if (existingCheckVoucherDetails.AccountName.TrimStart().TrimEnd() != worksheet6.Cells[cvdRow, 2].Text.TrimStart().TrimEnd())
                                {
                                    cvdChanges["AccountName"] = (existingCheckVoucherDetails.AccountName.TrimStart().TrimEnd(), worksheet6.Cells[cvdRow, 2].Text.TrimStart().TrimEnd());
                                }

                                if (existingCheckVoucherDetails.CheckVoucherHeader!.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet6.Cells[cvdRow, 3].Text.TrimStart().TrimEnd())
                                {
                                    cvdChanges["TransactionNo"] = (existingCheckVoucherDetails.CheckVoucherHeader.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet6.Cells[cvdRow, 3].Text.TrimStart().TrimEnd());
                                }

                                if (existingCheckVoucherDetails.Debit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet6.Cells[cvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvdChanges["Debit"] = (existingCheckVoucherDetails.Debit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet6.Cells[cvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCheckVoucherDetails.Credit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet6.Cells[cvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvdChanges["Credit"] = (existingCheckVoucherDetails.Credit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet6.Cells[cvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCheckVoucherDetails.CheckVoucherHeader.OriginalDocumentId.ToString("F0").TrimStart().TrimEnd() != decimal.Parse(worksheet6.Cells[cvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd())
                                {
                                    cvdChanges["CVHeaderId"] = (existingCheckVoucherDetails.CheckVoucherHeader.OriginalDocumentId.ToString("F0").TrimStart().TrimEnd(), decimal.Parse(worksheet6.Cells[cvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd());
                                }

                                if (cvdChanges.Any())
                                {
                                    await _checkVoucherRepo.LogChangesForCVDAsync(existingCheckVoucherDetails.OriginalDocumentId, cvdChanges, _userManager.GetUserName(this.User), existingCheckVoucherDetails.TransactionNo);
                                }
                            }
                        }
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Check Voucher Details Import --

                    #region -- Journal Voucher Header Import --

                    var rowCount = worksheet.Dimension.Rows;
                    var jvDictionary = new Dictionary<string, bool>();
                    var journalVoucherHeaderList = await _dbContext
                        .JournalVoucherHeaders
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        var journalVoucherHeader = new JournalVoucherHeader
                        {
                            JournalVoucherHeaderNo = worksheet.Cells[row, 10].Text,
                            Date = DateOnly.TryParse(worksheet.Cells[row, 1].Text, out DateOnly date) ? date : default,
                            References = worksheet.Cells[row, 2].Text,
                            Particulars = worksheet.Cells[row, 3].Text,
                            CRNo = worksheet.Cells[row, 4].Text,
                            JVReason = worksheet.Cells[row, 5].Text,
                            CreatedBy = worksheet.Cells[row, 6].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 7].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet.Cells[row, 12].Text,
                            PostedDate = DateTime.TryParse(worksheet.Cells[row, 13].Text, out DateTime postedDate) ? postedDate : default,
                            CancellationRemarks = worksheet.Cells[row, 8].Text,
                            OriginalCVId = int.TryParse(worksheet.Cells[row, 9].Text, out int originalCvId) ? originalCvId : 0,
                            OriginalSeriesNumber = worksheet.Cells[row, 10].Text,
                            OriginalDocumentId = int.TryParse(worksheet.Cells[row, 11].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        var jvhList = journalVoucherHeaderList.Any(cv => cv.OriginalDocumentId == journalVoucherHeader.OriginalDocumentId);
                        var existingJournalVoucher = await _dbContext.JournalVoucherHeaders.FirstOrDefaultAsync(si => si.OriginalDocumentId == journalVoucherHeader.OriginalDocumentId, cancellationToken);
                        jvDictionary.TryAdd((existingJournalVoucher != null ? existingJournalVoucher.JournalVoucherHeaderNo : journalVoucherHeader.JournalVoucherHeaderNo) ?? journalVoucherHeader.JournalVoucherHeaderNo, jvhList);

                        if (jvhList)
                        {
                            var jvChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();

                            if (existingJournalVoucher!.JournalVoucherHeaderNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 10].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["JVNo"] = (existingJournalVoucher.JournalVoucherHeaderNo.TrimStart().TrimEnd(), worksheet.Cells[row, 10].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["Date"] = (existingJournalVoucher.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.References!.TrimStart().TrimEnd() != worksheet.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["References"] = (existingJournalVoucher.References.TrimStart().TrimEnd(), worksheet.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.Particulars.TrimStart().TrimEnd() != worksheet.Cells[row, 3].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["Particulars"] = (existingJournalVoucher.Particulars.TrimStart().TrimEnd(), worksheet.Cells[row, 3].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.CRNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 4].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["CRNo"] = (existingJournalVoucher.CRNo.TrimStart().TrimEnd(), worksheet.Cells[row, 4].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.JVReason.TrimStart().TrimEnd() != worksheet.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["JVReason"] = (existingJournalVoucher.JVReason.TrimStart().TrimEnd(), worksheet.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.CreatedBy!.TrimStart().TrimEnd() != worksheet.Cells[row, 6].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["CreatedBy"] = (existingJournalVoucher.CreatedBy.TrimStart().TrimEnd(), worksheet.Cells[row, 6].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet.Cells[row, 7].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["CreatedDate"] = (existingJournalVoucher.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet.Cells[row, 7].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingJournalVoucher.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingJournalVoucher.CancellationRemarks.TrimStart().TrimEnd()) != worksheet.Cells[row, 8].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["CancellationRemarks"] = (existingJournalVoucher.CancellationRemarks?.TrimStart().TrimEnd(), worksheet.Cells[row, 8].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingJournalVoucher.OriginalCVId.ToString()!.TrimStart().TrimEnd() != worksheet.Cells[row, 9].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["OriginalCVId"] = (existingJournalVoucher.OriginalCVId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 9].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet.Cells[row, 10].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["OriginalSeriesNumber"] = (existingJournalVoucher.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet.Cells[row, 10].Text.TrimStart().TrimEnd());
                            }

                            if (existingJournalVoucher.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                jvChanges["OriginalDocumentId"] = (existingJournalVoucher.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (jvChanges.Any())
                            {
                                await _journalVoucherRepo.LogChangesAsync(existingJournalVoucher.OriginalDocumentId, jvChanges, _userManager.GetUserName(this.User), existingJournalVoucher.JournalVoucherHeaderNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!journalVoucherHeader.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(journalVoucherHeader.CreatedBy, $"Create new journal vouchcer# {journalVoucherHeader.JournalVoucherHeaderNo}", "Journal Voucher", ipAddress!, journalVoucherHeader.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!journalVoucherHeader.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(journalVoucherHeader.PostedBy, $"Posted journal voucher# {journalVoucherHeader.JournalVoucherHeaderNo}", "journal Voucher", ipAddress!, journalVoucherHeader.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        journalVoucherHeader.CVId = await _dbContext.CheckVoucherHeaders
                            .Where(c => c.OriginalDocumentId == journalVoucherHeader.OriginalCVId)
                            .Select(c => (int?)c.CheckVoucherHeaderId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the check voucher first.");

                        await _dbContext.JournalVoucherHeaders.AddAsync(journalVoucherHeader, cancellationToken);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Journal Voucher Header Import --

                    #region -- Journal Voucher Details Import --

                    if(journalVoucherHeaderList.Count >= 0)
                    {
                        var jvdRowCount = worksheet2.Dimension.Rows;

                        for (int jvdRow = 2; jvdRow <= jvdRowCount; jvdRow++)
                        {
                            var jvRow = jvdRow;
                            var journalVoucherDetails = new JournalVoucherDetail
                            {
                                AccountNo = worksheet2.Cells[jvdRow, 1].Text,
                                AccountName = worksheet2.Cells[jvdRow, 2].Text,
                                Debit = decimal.TryParse(worksheet2.Cells[jvdRow, 4].Text, out decimal debit) ? debit : 0,
                                Credit = decimal.TryParse(worksheet2.Cells[jvdRow, 5].Text, out decimal credit) ? credit : 0,
                                OriginalDocumentId = int.TryParse(worksheet2.Cells[jvdRow, 7].Text, out int originalDocumentId) ? originalDocumentId : 0
                            };

                            var jvHeader = await _dbContext.JournalVoucherHeaders
                                .Where(jvh => jvh.OriginalSeriesNumber == worksheet2.Cells[jvRow, 3].Text)
                                .FirstOrDefaultAsync(cancellationToken);

                            if (jvHeader != null)
                            {
                                journalVoucherDetails.JournalVoucherHeaderId = jvHeader.JournalVoucherHeaderId;
                                journalVoucherDetails.TransactionNo = jvHeader.JournalVoucherHeaderNo!;
                            }

                            if (jvDictionary.TryGetValue(journalVoucherDetails.TransactionNo, out var value) && !value)
                            {
                                await _dbContext.JournalVoucherDetails.AddAsync(journalVoucherDetails, cancellationToken);
                            }

                            if (jvDictionary.TryGetValue(journalVoucherDetails.TransactionNo, out var boolean) && boolean)
                            {
                                var jvdChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                                var existingJournalVoucherDetails = await _dbContext.JournalVoucherDetails
                                    .Include(cvd => cvd.JournalVoucherHeader)
                                    .FirstOrDefaultAsync(cvd => cvd.OriginalDocumentId == journalVoucherDetails.OriginalDocumentId, cancellationToken);

                                if (existingJournalVoucherDetails != null)
                                {
                                    if (existingJournalVoucherDetails.AccountNo.TrimStart().TrimEnd() != worksheet2.Cells[jvdRow, 1].Text.TrimStart().TrimEnd())
                                    {
                                        jvdChanges["AccountNo"] = (existingJournalVoucherDetails.AccountNo.TrimStart().TrimEnd(),
                                            worksheet2.Cells[jvdRow, 1].Text.TrimStart().TrimEnd());
                                    }

                                    if (existingJournalVoucherDetails.AccountName.TrimStart().TrimEnd() != worksheet2.Cells[jvdRow, 2].Text.TrimStart().TrimEnd())
                                    {
                                        jvdChanges["AccountName"] = (existingJournalVoucherDetails.AccountName.TrimStart().TrimEnd(), worksheet2.Cells[jvdRow, 2].Text.TrimStart().TrimEnd());
                                    }

                                    if (existingJournalVoucherDetails.JournalVoucherHeader.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet2.Cells[jvdRow, 3].Text.TrimStart().TrimEnd())
                                    {
                                        jvdChanges["TransactionNo"] = (existingJournalVoucherDetails.JournalVoucherHeader.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet2.Cells[jvdRow, 3].Text.TrimStart().TrimEnd());
                                    }

                                    if (existingJournalVoucherDetails.Debit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[jvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd())
                                    {
                                        jvdChanges["Debit"] = (existingJournalVoucherDetails.Debit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[jvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd());
                                    }

                                    if (existingJournalVoucherDetails.Credit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[jvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd())
                                    {
                                        jvdChanges["Credit"] = (existingJournalVoucherDetails.Credit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[jvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd());
                                    }

                                    if (existingJournalVoucherDetails.JournalVoucherHeader.OriginalDocumentId.ToString("F0").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[jvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd())
                                    {
                                        jvdChanges["JVHeaderId"] = (existingJournalVoucherDetails.JournalVoucherHeader.OriginalDocumentId.ToString("F0").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[jvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd());
                                    }

                                    if (jvdChanges.Any())
                                    {
                                        await _journalVoucherRepo.LogChangesForJVDAsync(existingJournalVoucherDetails.OriginalDocumentId, jvdChanges, _userManager.GetUserName(this.User), existingJournalVoucherDetails.TransactionNo);
                                    }
                                }
                            }
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);

                        var checkChangesOfRecord = await _dbContext.ImportExportLogs
                            .Where(iel => iel.Action == string.Empty).ToListAsync(cancellationToken);
                        if (checkChangesOfRecord.Any())
                        {
                            TempData["importChanges"] = "";
                        }
                    }

                    #endregion -- Journal Voucher Details Import --
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                }
                catch (InvalidOperationException ioe)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["warning"] = ioe.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.JournalVoucher });
        }

        #endregion -- import xlsx record --
    }
}
