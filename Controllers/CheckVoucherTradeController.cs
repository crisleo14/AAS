using System.Globalization;
using Accounting_System.Data;
using Accounting_System.Repository;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Accounting_System.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using System.Linq.Dynamic.Core;
using Accounting_System.Models.Reports;
using Accounting_System.Utility;
using Microsoft.IdentityModel.Tokens;

namespace Accounting_System.Controllers
{
    public class CheckVoucherTradeController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly ILogger<CheckVoucherTradeController> _logger;

        private readonly GeneralRepo _generalRepo;

        private readonly CheckVoucherRepo _checkVoucherRepo;

        private readonly ReceivingReportRepo _receivingReportRepo;

        private readonly PurchaseOrderRepo _purchaseOrderRepo;

        public CheckVoucherTradeController(UserManager<IdentityUser> userManager,
            ApplicationDbContext dbContext,
            IWebHostEnvironment webHostEnvironment,
            ILogger<CheckVoucherTradeController> logger,
            GeneralRepo generalRepo,
            CheckVoucherRepo checkVoucherRepo,
            PurchaseOrderRepo purchaseOrderRepo,
            ReceivingReportRepo receivingReportRepo)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _generalRepo = generalRepo;
            _checkVoucherRepo = checkVoucherRepo;
            _purchaseOrderRepo = purchaseOrderRepo;
            _receivingReportRepo = receivingReportRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            if (view == nameof(DynamicView.CheckVoucher))
            {
                var checkVoucherHeaders = await _dbContext.CheckVoucherHeaders
                    .Where(cv => cv.CvType != "Payment")
                    .ToListAsync(cancellationToken);

                return View("ImportExportIndex", checkVoucherHeaders);
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetCheckVouchers([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var checkVoucherHeaders = await _checkVoucherRepo.GetCheckVouchersAsync(cancellationToken);

                // Search filter
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();

                    checkVoucherHeaders = checkVoucherHeaders
                        .Where(cv =>
                            cv.CheckVoucherHeaderNo!.ToLower().Contains(searchValue) ||
                            cv.Date.ToString(CS.Date_Format).ToLower().Contains(searchValue) ||
                            cv.Supplier?.SupplierName.ToLower().Contains(searchValue) == true ||
                            cv.Total.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            cv.Amount?.ToString()?.Contains(searchValue) == true ||
                            cv.AmountPaid.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            cv.Category.ToLower().Contains(searchValue) ||
                            cv.CvType?.ToLower().Contains(searchValue) == true ||
                            cv.CreatedBy!.ToLower().Contains(searchValue)
                        )
                    .ToList();
                }

                // Sorting
                if (parameters.Order?.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";

                    checkVoucherHeaders = checkVoucherHeaders
                        .AsQueryable()
                        .OrderBy($"{columnName} {sortDirection}")
                        .ToList();
                }

                var totalRecords = checkVoucherHeaders.Count();

                var pagedData = checkVoucherHeaders
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
                _logger.LogError(ex, "Failed to get check vouchers. Error: {ErrorMessage}, Stack: {StackTrace}.",
                    ex.Message, ex.StackTrace);
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            CheckVoucherTradeViewModel model = new()
            {
                COA = await _dbContext.ChartOfAccounts
                    .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken),
                Suppliers = await _dbContext.Suppliers
                    .Where(supp => supp.Category == "Trade")
                    .OrderBy(supp => supp.Number)
                    .Select(sup => new SelectListItem
                    {
                        Value = sup.SupplierId.ToString(),
                        Text = sup.SupplierName
                    })
                    .ToListAsync(cancellationToken: cancellationToken),
                BankAccounts = await _dbContext.BankAccounts
                    .Select(ba => new SelectListItem
                    {
                        Value = ba.BankAccountId.ToString(),
                        Text = ba.Bank + " " + ba.AccountName
                    })
                    .ToListAsync(cancellationToken: cancellationToken)
            };

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CheckVoucherTradeViewModel viewModel, IFormFile? file, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    #region --Check if duplicate record

                    if (!viewModel.CheckNo.Any() && !viewModel.CheckNo.Contains("DM"))
                    {
                        var cv = await _dbContext
                        .CheckVoucherHeaders
                        .Where(cv => cv.CheckNo == viewModel.CheckNo && cv.BankId == viewModel.BankId)
                        .ToListAsync(cancellationToken);
                        if (cv.Any())
                        {
                            viewModel.COA = await _dbContext.ChartOfAccounts
                                .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                                .Select(s => new SelectListItem
                                {
                                    Value = s.AccountNumber,
                                    Text = s.AccountNumber + " " + s.AccountName
                                })
                                .ToListAsync(cancellationToken);

                            viewModel.Suppliers = await _dbContext.Suppliers
                                .Where(supp => supp.Category == "Trade")
                                .Select(sup => new SelectListItem
                                {
                                    Value = sup.SupplierId.ToString(),
                                    Text = sup.SupplierName
                                })
                                .ToListAsync(cancellationToken: cancellationToken);

                            viewModel.PONo = await _dbContext.PurchaseOrders
                                .Where(po => po.SupplierId == viewModel.SupplierId && po.IsPosted)
                                .Select(po => new SelectListItem
                                {
                                    Value = po.PurchaseOrderNo!.ToString(),
                                    Text = po.PurchaseOrderNo
                                })
                                .ToListAsync(cancellationToken);

                            viewModel.BankAccounts = await _dbContext.BankAccounts
                                .Select(ba => new SelectListItem
                                {
                                    Value = ba.BankAccountId.ToString(),
                                    Text = ba.Bank + " " + ba.AccountName
                                })
                                .ToListAsync(cancellationToken: cancellationToken);

                            TempData["error"] = "Check No. Is already exist";
                            return View(viewModel);
                        }
                    }

                    #endregion --Check if duplicate record

                    #region --Retrieve Supplier

                    await _dbContext
                        .Suppliers
                        .FirstOrDefaultAsync(po => po.SupplierId == viewModel.SupplierId, cancellationToken);

                    #endregion --Retrieve Supplier

                    #region -- Get PO --

                    await _dbContext.PurchaseOrders
                        .Where(po => viewModel.POSeries != null && viewModel.POSeries.Contains(po.PurchaseOrderNo))
                        .FirstOrDefaultAsync(cancellationToken: cancellationToken);

                    #endregion -- Get PO --

                    #region --Saving the default entries

                    var generateCvNo = await _checkVoucherRepo.GenerateCVNo(cancellationToken);
                    var cashInBank = viewModel.Credit[1];
                    var cvh = new CheckVoucherHeader
                    {
                        CheckVoucherHeaderNo = generateCvNo,
                        Date = viewModel.TransactionDate,
                        PONo = viewModel.POSeries,
                        SupplierId = viewModel.SupplierId,
                        Particulars = viewModel.Particulars,
                        BankId = viewModel.BankId,
                        CheckNo = viewModel.CheckNo,
                        Category = "Trade",
                        Payee = viewModel.Payee,
                        CheckDate = viewModel.CheckDate,
                        Total = cashInBank,
                        CreatedBy = _userManager.GetUserName(this.User),
                        CvType = "Supplier",
                        // Address = supplier.SupplierAddress,
                        // Tin = supplier.SupplierTin,
                    };

                    await _dbContext.CheckVoucherHeaders.AddAsync(cvh, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion --Saving the default entries

                    #region --CV Details Entry

                    var cvDetails = new List<CheckVoucherDetail>();
                    for (int i = 0; i < viewModel.AccountNumber.Length; i++)
                    {
                        if (viewModel.Debit[i] != 0 || viewModel.Credit[i] != 0)
                        {
                            cvDetails.Add(
                            new CheckVoucherDetail
                            {
                                AccountNo = viewModel.AccountNumber[i],
                                AccountName = viewModel.AccountTitle[i],
                                Debit = viewModel.Debit[i],
                                Credit = viewModel.Credit[i],
                                TransactionNo = cvh.CheckVoucherHeaderNo,
                                CheckVoucherHeaderId = cvh.CheckVoucherHeaderId,
                                SupplierId = i == 0 ? viewModel.SupplierId : null
                            });
                        }
                    }

                    await _dbContext.CheckVoucherDetails.AddRangeAsync(cvDetails, cancellationToken);

                    #endregion --CV Details Entry

                    #region -- Partial payment of RR's

                    var cvTradePaymentModel = new List<CVTradePayment>();
                    foreach (var item in viewModel.RRs)
                    {
                        var getReceivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.Id, cancellationToken);
                        getReceivingReport!.AmountPaid += item.Amount;

                        cvTradePaymentModel.Add(
                            new CVTradePayment
                            {
                                DocumentId = getReceivingReport.ReceivingReportId,
                                DocumentType = "RR",
                                CheckVoucherId = cvh.CheckVoucherHeaderId,
                                AmountPaid = item.Amount
                            });
                    }

                    await _dbContext.AddRangeAsync(cvTradePaymentModel, cancellationToken);

                    #endregion -- Partial payment of RR's

                    #region -- Uploading file --

                    if (file?.Length > 0)
                    {
                        var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Supporting CV Files",
                            cvh.CheckVoucherHeaderNo);

                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var fileName = Path.GetFileName(file.FileName);
                        var fileSavePath = Path.Combine(uploadsFolder, fileName);

                        await using FileStream stream = new FileStream(fileSavePath, FileMode.Create);
                        await file.CopyToAsync(stream, cancellationToken);

                        //if necessary add field to store location path
                        // model.Header.SupportingFilePath = fileSavePath
                    }

                    #region --Audit Trail Recording

                    if (cvh.OriginalSeriesNumber.IsNullOrEmpty() && cvh.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(cvh.CreatedBy!,
                            $"Create new check voucher# {cvh.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    TempData["success"] = "Check voucher trade created successfully";
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(Index));

                    #endregion -- Uploading file --
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create check voucher. Error: {ErrorMessage}, Stack: {StackTrace}. Created by: {UserName}",
                        ex.Message, ex.StackTrace, _userManager.GetUserName(User));
                    viewModel.COA = await _dbContext.ChartOfAccounts
                        .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                        .Select(s => new SelectListItem
                        {
                            Value = s.AccountNumber,
                            Text = s.AccountNumber + " " + s.AccountName
                        })
                        .ToListAsync(cancellationToken);

                    viewModel.Suppliers = await _dbContext.Suppliers
                            .Where(supp => supp.Category == "Trade")
                            .Select(sup => new SelectListItem
                            {
                                Value = sup.SupplierId.ToString(),
                                Text = sup.SupplierName
                            })
                            .ToListAsync(cancellationToken: cancellationToken);

                    viewModel.PONo = await _dbContext.PurchaseOrders
                                .Where(po => po.SupplierId == viewModel.SupplierId && po.IsPosted)
                                .Select(po => new SelectListItem
                                {
                                    Value = po.PurchaseOrderNo!.ToString(),
                                    Text = po.PurchaseOrderNo
                                })
                                .ToListAsync(cancellationToken);

                    viewModel.BankAccounts = await _dbContext.BankAccounts
                        .Select(ba => new SelectListItem
                        {
                            Value = ba.BankAccountId.ToString(),
                            Text = ba.Bank + " " + ba.AccountName
                        })
                        .ToListAsync(cancellationToken: cancellationToken);

                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(viewModel);
                }
            }
            viewModel.COA = await _dbContext.ChartOfAccounts
                .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            viewModel.Suppliers = await _dbContext.Suppliers
                .Where(supp => supp.Category == "Trade")
                .Select(sup => new SelectListItem
                {
                    Value = sup.SupplierId.ToString(),
                    Text = sup.SupplierName
                })
                .ToListAsync(cancellationToken: cancellationToken);

            viewModel.PONo = await _dbContext.PurchaseOrders
                .Where(po => po.SupplierId == viewModel.SupplierId && po.IsPosted)
                .Select(po => new SelectListItem
                {
                    Value = po.PurchaseOrderNo!.ToString(),
                    Text = po.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);

            viewModel.BankAccounts = await _dbContext.BankAccounts
                .Select(ba => new SelectListItem
                {
                    Value = ba.BankAccountId.ToString(),
                    Text = ba.Bank + " " + ba.AccountName
                })
                .ToListAsync(cancellationToken: cancellationToken);

            TempData["error"] = "The information provided was invalid.";
            return View(viewModel);
        }

        public async Task<IActionResult> GetPOs(int supplierId)
        {
            var purchaseOrders = await _dbContext.PurchaseOrders
                .Where(po => po.SupplierId == supplierId && po.IsPosted)
                .ToListAsync();

            if (purchaseOrders.Any())
            {
                var poList = purchaseOrders.OrderBy(po => po.PurchaseOrderNo)
                                        .Select(po => new { Id = po.PurchaseOrderId, PONumber = po.PurchaseOrderNo })
                                        .ToList();
                return Json(poList);
            }

            return Json(null);
        }

        public async Task<IActionResult> GetRRs(string[] poNumber, int? cvId, CancellationToken cancellationToken)
        {
            var query = _dbContext.ReceivingReports
                .Where(rr => !rr.IsPaid
                             && poNumber.Contains(rr.PONo)
                             && rr.IsPosted);

            if (cvId != null)
            {
                var rrIds = await _dbContext.CVTradePayments
                    .Where(cvp => cvp.CheckVoucherId == cvId && cvp.DocumentType == "RR")
                    .Select(cvp => cvp.DocumentId)
                    .ToListAsync(cancellationToken);

                query = query.Union(_dbContext.ReceivingReports
                    .Where(rr => poNumber.Contains(rr.PONo) && rrIds.Contains(rr.ReceivingReportId)));
            }

            var receivingReports = await query
                .Include(rr => rr.PurchaseOrder)
                .ThenInclude(rr => rr!.Supplier)
                .OrderBy(rr => rr.ReceivingReportNo)
                .ToListAsync(cancellationToken);

            if (receivingReports.Any())
            {
                var rrList = receivingReports
                    .Select(rr => {
                        var netOfVatAmount = _generalRepo.ComputeNetOfVat(rr.Amount);

                        var ewtAmount = rr.PurchaseOrder?.Supplier?.TaxType == CS.TaxType_WithTax
                            ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m)
                            : 0.0000m;

                        var netOfEwtAmount = rr.PurchaseOrder?.Supplier?.TaxType == CS.TaxType_WithTax
                            ? _generalRepo.ComputeNetOfEwt(rr.Amount, ewtAmount)
                            : netOfVatAmount;

                        return new {
                            Id = rr.ReceivingReportId,
                            rr.ReceivingReportNo,
                            AmountPaid = rr.AmountPaid.ToString(CS.Two_Decimal_Format),
                            NetOfEwtAmount = netOfEwtAmount.ToString(CS.Two_Decimal_Format)
                        };
                    }).ToList();
                return Json(rrList);
            }

            return Json(null);
        }

        public async Task<IActionResult> GetSupplierDetails(int? supplierId)
        {
            if (supplierId != null)
            {
                var supplier = await _dbContext.Suppliers
                    .FindAsync(supplierId);

                if (supplier != null)
                {
                    return Json(new
                    {
                        Name = supplier.SupplierName,
                        Address = supplier.SupplierAddress,
                        TinNo = supplier.SupplierTin,
                        supplier.TaxType,
                        supplier.Category,
                        TaxPercent = supplier.WithholdingTaxPercent,
                        supplier.VatType,
                        DefaultExpense = supplier.DefaultExpenseNumber,
                        WithholdingTax = supplier.WithholdingTaxtitle
                    });
                }
            }
            return Json(null);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                return NotFound();
            }

            var existingHeaderModel = await _dbContext.CheckVoucherHeaders
                .FirstOrDefaultAsync(cvh => cvh.CheckVoucherHeaderId == id, cancellationToken);

            var existingDetailsModel = await _dbContext.CheckVoucherDetails
                .Where(cvd => cvd.CheckVoucherHeaderId == existingHeaderModel!.CheckVoucherHeaderId)
                .ToListAsync(cancellationToken);

            if (existingHeaderModel == null || !existingDetailsModel.Any())
            {
                return NotFound();
            }

            CheckVoucherTradeViewModel model = new()
            {
                SupplierId = existingHeaderModel.SupplierId ?? 0,
                Payee = existingHeaderModel.Payee!,
                // SupplierAddress = existingHeaderModel.Address,
                // SupplierTinNo = existingHeaderModel.Tin,
                POSeries = existingHeaderModel.PONo,
                TransactionDate = existingHeaderModel.Date,
                BankId = existingHeaderModel.BankId,
                CheckNo = existingHeaderModel.CheckNo!,
                CheckDate = existingHeaderModel.CheckDate ?? DateOnly.MinValue,
                Particulars = existingHeaderModel.Particulars!,
                CVId = existingHeaderModel.CheckVoucherHeaderId,
                CVNo = existingHeaderModel.CheckVoucherHeaderNo,
                CreatedBy = _userManager.GetUserName(this.User),
                RRs = new List<ReceivingReportList>(),
                Suppliers = await _dbContext.Suppliers
                    .Where(supp => supp.Category == "Trade")
                    .OrderBy(supp => supp.Number)
                    .Select(sup => new SelectListItem
                    {
                        Value = sup.SupplierId.ToString(),
                        Text = sup.SupplierName
                    })
                    .ToListAsync(cancellationToken: cancellationToken)
            };

            var getCheckVoucherTradePayment = await _dbContext.CVTradePayments
                .Where(cv => cv.CheckVoucherId == id && cv.DocumentType == "RR")
                .ToListAsync(cancellationToken);

            foreach (var item in getCheckVoucherTradePayment)
            {
                model.RRs.Add(new ReceivingReportList
                {
                    Id = item.DocumentId,
                    Amount = item.AmountPaid
                });
            }

            model.COA = await _dbContext.ChartOfAccounts
                .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            model.PONo = await _dbContext.PurchaseOrders
                .OrderBy(s => s.PurchaseOrderNo)
                .Select(s => new SelectListItem
                {
                    Value = s.PurchaseOrderNo,
                    Text = s.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);

            model.BankAccounts = await _dbContext.BankAccounts
                .Select(ba => new SelectListItem
                {
                    Value = ba.BankAccountId.ToString(),
                    Text = ba.Bank + " " + ba.AccountName
                })
                .ToListAsync(cancellationToken: cancellationToken);

            return View(model);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(CheckVoucherTradeViewModel viewModel, IFormFile? file, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var existingHeaderModel = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cv => cv.CheckVoucherHeaderId == viewModel.CVId, cancellationToken);

                try
                {
                    #region --CV Details Entry

                    var existingDetailsModel = await _dbContext.CheckVoucherDetails
                        .Where(d => d.CheckVoucherHeaderId == existingHeaderModel!.CheckVoucherHeaderId)
                        .ToListAsync(cancellationToken: cancellationToken);

                    _dbContext.RemoveRange(existingDetailsModel);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    var details = new List<CheckVoucherDetail>();

                    var cashInBank = 0m;
                    for (int i = 0; i < viewModel.AccountTitle.Length; i++)
                    {
                        cashInBank = viewModel.Credit[1];
                        var getOriginalDocumentId =
                            existingDetailsModel.FirstOrDefault(x => x.AccountName == viewModel.AccountTitle[i]);

                        details.Add(new CheckVoucherDetail
                        {
                            AccountNo = viewModel.AccountNumber[i],
                            AccountName = viewModel.AccountTitle[i],
                            Debit = viewModel.Debit[i],
                            Credit = viewModel.Credit[i],
                            TransactionNo = existingHeaderModel!.CheckVoucherHeaderNo!,
                            CheckVoucherHeaderId = viewModel.CVId,
                            SupplierId = i == 0 ? viewModel.SupplierId : null,
                            OriginalDocumentId = getOriginalDocumentId?.OriginalDocumentId
                        });
                    }

                    await _dbContext.CheckVoucherDetails.AddRangeAsync(details, cancellationToken);

                    #endregion --CV Details Entry

                    #region --Saving the default entries

                    existingHeaderModel!.Date = viewModel.TransactionDate;
                    existingHeaderModel.PONo = viewModel.POSeries;
                    existingHeaderModel.SupplierId = viewModel.SupplierId;
                    // existingHeaderModel.Address = viewModel.SupplierAddress;
                    // existingHeaderModel.Tin = viewModel.SupplierTinNo;
                    existingHeaderModel.Particulars = viewModel.Particulars;
                    existingHeaderModel.BankId = viewModel.BankId;
                    existingHeaderModel.CheckNo = viewModel.CheckNo;
                    existingHeaderModel.Category = "Trade";
                    existingHeaderModel.Payee = viewModel.Payee;
                    existingHeaderModel.CheckDate = viewModel.CheckDate;
                    existingHeaderModel.Total = cashInBank;
                    // existingHeaderModel.EditedBy = _userManager.GetUserName(User);
                    // existingHeaderModel.EditedDate = DateTimeHelper.GetCurrentPhilippineTime();

                    #endregion --Saving the default entries

                    #region -- Partial payment of RR's

                    var getCheckVoucherTradePayment = await _dbContext.CVTradePayments
                        .Where(cv => cv.CheckVoucherId == existingHeaderModel.CheckVoucherHeaderId && cv.DocumentType == "RR")
                        .ToListAsync(cancellationToken);

                    foreach (var item in getCheckVoucherTradePayment)
                    {
                        var recevingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.DocumentId, cancellationToken);

                        recevingReport!.AmountPaid -= item.AmountPaid;
                    }

                    _dbContext.RemoveRange(getCheckVoucherTradePayment);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    var cvTradePaymentModel = new List<CVTradePayment>();
                    foreach (var item in viewModel.RRs)
                    {
                        var getReceivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.Id, cancellationToken);
                        getReceivingReport!.AmountPaid += item.Amount;

                        cvTradePaymentModel.Add(
                            new CVTradePayment
                            {
                                DocumentId = getReceivingReport.ReceivingReportId,
                                DocumentType = "RR",
                                CheckVoucherId = existingHeaderModel.CheckVoucherHeaderId,
                                AmountPaid = item.Amount
                            });
                    }

                    await _dbContext.AddRangeAsync(cvTradePaymentModel, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Partial payment of RR's

                    #region -- Uploading file --

                    if (file?.Length > 0)
                    {
                        var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Supporting CV Files",
                            existingHeaderModel.CheckVoucherHeaderNo!);

                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var fileName = Path.GetFileName(file.FileName);
                        var fileSavePath = Path.Combine(uploadsFolder, fileName);

                        await using FileStream stream = new FileStream(fileSavePath, FileMode.Create);
                        await file.CopyToAsync(stream, cancellationToken);

                        //if necessary add field to store location path
                        // model.Header.SupportingFilePath = fileSavePath
                    }

                    #endregion -- Uploading file --

                    #region --Audit Trail Recording

                    if (existingHeaderModel.OriginalSeriesNumber.IsNullOrEmpty() && existingHeaderModel.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(existingHeaderModel.CreatedBy!,
                            $"Edited check voucher# {existingHeaderModel.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);  // await the SaveChangesAsync method
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Trade edited successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to edit check voucher. Error: {ErrorMessage}, Stack: {StackTrace}. Edited by: {UserName}",
                        ex.Message, ex.StackTrace, _userManager.GetUserName(User));
                    viewModel.COA = await _dbContext.ChartOfAccounts
                        .Where(coa => !new[] { "202010200", "202010100", "101010100" }.Any(excludedNumber => coa.AccountNumber != null && coa.AccountNumber.Contains(excludedNumber)) && !coa.HasChildren)
                        .Select(s => new SelectListItem
                        {
                            Value = s.AccountNumber,
                            Text = s.AccountNumber + " " + s.AccountName
                        })
                        .ToListAsync(cancellationToken);

                    viewModel.PONo = await _dbContext.PurchaseOrders
                        .OrderBy(s => s.PurchaseOrderNo)
                        .Select(s => new SelectListItem
                        {
                            Value = s.PurchaseOrderNo,
                            Text = s.PurchaseOrderNo
                        })
                        .ToListAsync(cancellationToken);

                    viewModel.BankAccounts = await _dbContext.BankAccounts
                        .Select(ba => new SelectListItem
                        {
                            Value = ba.BankAccountId.ToString(),
                            Text = ba.Bank + " " + ba.AccountName
                        })
                        .ToListAsync(cancellationToken: cancellationToken);

                    viewModel.Suppliers = await _dbContext.Suppliers
                            .OrderBy(s => s.Number)
                            .Select(s => new SelectListItem
                            {
                                Value = s.SupplierId.ToString(),
                                Text = s.Number + " " + s.SupplierName
                            })
                            .ToListAsync(cancellationToken);

                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(viewModel);
                }
            }

            TempData["error"] = "The information provided was invalid.";
            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Print(int? id, int? supplierId, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                return NotFound();
            }

            var header = await _dbContext.CheckVoucherHeaders
                .Include(cvh => cvh.Supplier)
                .FirstOrDefaultAsync(cvh => cvh.CheckVoucherHeaderId == id.Value, cancellationToken);

            if (header == null)
            {
                return NotFound();
            }

            var details = await _dbContext.CheckVoucherDetails
                .Include(cvd => cvd.Supplier)
                .Where(cvd => cvd.CheckVoucherHeaderId == header.CheckVoucherHeaderId)
                .ToListAsync(cancellationToken);

            var getSupplier = await _dbContext.Suppliers
                .FirstOrDefaultAsync(x => x.SupplierId == supplierId, cancellationToken);

            if (header.Category == "Trade" && header.RRNo != null)
            {
                var siArray = new string[header.RRNo.Length];
                for (int i = 0; i < header.RRNo.Length; i++)
                {
                    var rrValue = header.RRNo[i];

                    var rr = await _dbContext.ReceivingReports
                                .FirstOrDefaultAsync(p => p.ReceivingReportNo == rrValue, cancellationToken: cancellationToken);

                    if (rr != null)
                    {
                        siArray[i] = rr.SupplierInvoiceNumber!;
                    }
                }

                ViewBag.SINoArray = siArray;
            }

            var viewModel = new CheckVoucherVM
            {
                Header = header,
                Details = details,
                Supplier = getSupplier
            };

            return View(viewModel);
        }

        public async Task<IActionResult> Printed(int id, int? supplierId, CancellationToken cancellationToken)
        {
            var cv = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(x => x.CheckVoucherHeaderId == id, cancellationToken);
            if (!cv!.IsPrinted)
            {
                #region --Audit Trail Recording

                if (cv.OriginalSeriesNumber.IsNullOrEmpty() && cv.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    AuditTrail auditTrailBook = new(cv.CreatedBy!,
                        $"Printed original copy of check voucher# {cv.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                cv.IsPrinted = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(Print), new { id, supplierId });
        }

        public async Task<IActionResult> Post(int id, int? supplierId, CancellationToken cancellationToken)
        {
            var modelHeader = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cv => cv.CheckVoucherHeaderId == id, cancellationToken);
            var modelDetails = await _dbContext.CheckVoucherDetails.Where(cvd => cvd.CheckVoucherHeaderId == modelHeader!.CheckVoucherHeaderId).ToListAsync(cancellationToken: cancellationToken);
            var supplierName = await _dbContext.Suppliers.Where(s => s.SupplierId == supplierId).Select(s => s.SupplierName).FirstOrDefaultAsync(cancellationToken);

            if (modelHeader != null)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    if (!modelHeader.IsPosted)
                    {
                        modelHeader.PostedBy = _userManager.GetUserName(this.User);
                        modelHeader.PostedDate = DateTime.Now;
                        modelHeader.IsPosted = true;
                        //modelHeader.Status = nameof(Status.Posted);

                        #region -- Recalculate payment of RR's or DR's

                        var getCheckVoucherTradePayment = await _dbContext.CVTradePayments
                            .Where(cv => cv.CheckVoucherId == id)
                            .Include(cv => cv.CV)
                            .ToListAsync(cancellationToken);

                        foreach (var item in getCheckVoucherTradePayment)
                        {
                            if (item.DocumentType == "RR")
                            {
                                var receivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.DocumentId, cancellationToken);

                                receivingReport!.IsPaid = true;
                                receivingReport.PaidDate = DateTime.Now;
                            }
                        }

                        #endregion -- Recalculate payment of RR's or DR's

                        #region --General Ledger Book Recording(CV)--

                        var accountTitlesDto = await _generalRepo.GetListOfAccountTitleDto(cancellationToken);
                        var ledgers = new List<GeneralLedgerBook>();
                        foreach (var details in modelDetails)
                        {
                            var account = accountTitlesDto.Find(c => c.AccountNumber == details.AccountNo) ?? throw new ArgumentException($"Account title '{details.AccountNo}' not found.");
                            ledgers.Add(
                                    new GeneralLedgerBook
                                    {
                                        Date = modelHeader.Date,
                                        Reference = modelHeader.CheckVoucherHeaderNo!,
                                        Description = modelHeader.Particulars!,
                                        AccountNo = account.AccountNumber,
                                        AccountTitle = account.AccountName,
                                        Debit = details.Debit,
                                        Credit = details.Credit,
                                        CreatedBy = modelHeader.CreatedBy,
                                        CreatedDate = modelHeader.CreatedDate,
                                    }
                                );
                        }

                        if (!_generalRepo.IsJournalEntriesBalanced(ledgers))
                        {
                            throw new ArgumentException("Debit and Credit is not equal, check your entries.");
                        }

                        await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledgers, cancellationToken);

                        #endregion --General Ledger Book Recording(CV)--

                        #region --Disbursement Book Recording(CV)--

                        var disbursement = new List<DisbursementBook>();
                        foreach (var details in modelDetails)
                        {
                            var bank = _dbContext.BankAccounts.FirstOrDefault(model => model.BankAccountId == modelHeader.BankId);
                            disbursement.Add(
                                    new DisbursementBook
                                    {
                                        Date = modelHeader.Date,
                                        CVNo = modelHeader.CheckVoucherHeaderNo!,
                                        Payee = modelHeader.Payee ?? supplierName!,
                                        Amount = modelHeader.Total,
                                        Particulars = modelHeader.Particulars!,
                                        Bank = bank != null ? bank.Bank : "N/A",
                                        CheckNo = !string.IsNullOrEmpty(modelHeader.CheckNo) ? modelHeader.CheckNo : "N/A",
                                        CheckDate = modelHeader.CheckDate != null ? modelHeader.CheckDate?.ToString("MM/dd/yyyy")! : "N/A",
                                        ChartOfAccount = details.AccountNo + " " + details.AccountName,
                                        Debit = details.Debit,
                                        Credit = details.Credit,
                                        CreatedBy = modelHeader.CreatedBy,
                                        CreatedDate = modelHeader.CreatedDate
                                    }
                                );
                        }

                        await _dbContext.DisbursementBooks.AddRangeAsync(disbursement, cancellationToken);

                        #endregion --Disbursement Book Recording(CV)--

                        #region --Audit Trail Recording

                        if (modelHeader.OriginalSeriesNumber.IsNullOrEmpty() && modelHeader.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(modelHeader.CreatedBy!,
                                $"Posted check voucher# {modelHeader.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Check Voucher has been Posted.";
                    }
                    return RedirectToAction(nameof(Print), new { id, supplierId });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to post check voucher. Error: {ErrorMessage}, Stack: {StackTrace}. Posted by: {UserName}",
                        ex.Message, ex.StackTrace, _userManager.GetUserName(User));
                    await transaction.RollbackAsync(cancellationToken);

                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

            return NotFound();
        }

        public async Task<IActionResult> Cancel(int id, string? cancellationRemarks, CancellationToken cancellationToken)
        {
            var model = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(x => x.CheckVoucherHeaderId == id, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                if (model != null)
                {
                    if (!model.IsCanceled)
                    {
                        model.CanceledBy = _userManager.GetUserName(this.User);
                        model.CanceledDate = DateTime.Now;
                        model.IsCanceled = true;
                        //model.Status = nameof(Status.Canceled);
                        model.CancellationRemarks = cancellationRemarks;

                        #region -- Recalculate payment of RR's or DR's

                        var getCheckVoucherTradePayment = await _dbContext.CVTradePayments
                            .Where(cv => cv.CheckVoucherId == id)
                            .Include(cv => cv.CV)
                            .ToListAsync(cancellationToken);

                        foreach (var item in getCheckVoucherTradePayment)
                        {
                            if (item.DocumentType == "RR")
                            {
                                var receivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.DocumentId, cancellationToken);

                                receivingReport!.IsPaid = false;
                                receivingReport.AmountPaid -= item.AmountPaid;
                            }
                        }

                        #endregion -- Recalculate payment of RR's or DR's

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.CreatedBy!,
                                $"Canceled check voucher# {model.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);

                        TempData["success"] = "Check Voucher has been Cancelled.";
                    }

                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                _logger.LogError(ex, "Failed to cancel check voucher. Error: {ErrorMessage}, Stack: {StackTrace}. Canceled by: {UserName}",
                    ex.Message, ex.StackTrace, _userManager.GetUserName(User));
                TempData["error"] = $"Error: '{ex.Message}'";
                return RedirectToAction(nameof(Index));
            }

            return NotFound();
        }

        public async Task<IActionResult> Void(int id, CancellationToken cancellationToken)
        {
            var model = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(x => x.CheckVoucherHeaderId == id, cancellationToken);

            if (model != null)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    if (!model.IsVoided)
                    {
                        if (model.IsPosted)
                        {
                            model.IsPosted = false;
                        }

                        model.VoidedBy = _userManager.GetUserName(this.User);
                        model.VoidedDate = DateTime.Now;
                        model.IsVoided = true;
                        //model.Status = nameof(Status.Voided);

                        await _generalRepo.RemoveRecords<DisbursementBook>(db => db.CVNo == model.CheckVoucherHeaderNo, cancellationToken);
                        await _generalRepo.RemoveRecords<GeneralLedgerBook>(gl => gl.Reference == model.CheckVoucherHeaderNo, cancellationToken);

                        //re-compute amount paid in trade and payment voucher
                        #region -- Recalculate payment of RR's or DR's

                        var getCheckVoucherTradePayment = await _dbContext.CVTradePayments
                            .Where(cv => cv.CheckVoucherId == id)
                            .Include(cv => cv.CV)
                            .ToListAsync(cancellationToken);

                        foreach (var item in getCheckVoucherTradePayment)
                        {
                            if (item.DocumentType == "RR")
                            {
                                var receivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == item.DocumentId, cancellationToken);

                                receivingReport!.IsPaid = false;
                                receivingReport.AmountPaid -= item.AmountPaid;
                            }
                        }

                        #endregion -- Recalculate payment of RR's or DR's

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.CreatedBy!,
                                $"Voided check voucher# {model.CheckVoucherHeaderNo}", "Check Voucher Trade", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Check Voucher has been Voided.";

                        return RedirectToAction(nameof(Index));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to void check voucher. Error: {ErrorMessage}, Stack: {StackTrace}. Voided by: {UserName}",
                        ex.Message, ex.StackTrace, _userManager.GetUserName(User));
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

            return NotFound();
        }

        [HttpGet]
        public IActionResult GetAllCheckVoucherIds()
        {
            var cvIds = _dbContext.CheckVoucherHeaders
                                     .Select(cv => cv.CheckVoucherHeaderId) // Assuming Id is the primary key
                                     .ToList();

            return Json(cvIds);
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
                    if (file.FileName.Contains(CS.Name))
                    {
                        using var package = new ExcelPackage(stream);
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CheckVoucherHeader");

                        var worksheet2 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CheckVoucherDetails");

                        var worksheet3 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "PurchaseOrder");

                        var worksheet4 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "ReceivingReport");

                        var worksheet5 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CheckVoucherTradePayments");

                        var worksheet6 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "MultipleCheckVoucherPayments");

                        if (worksheet == null)
                        {
                            TempData["error"] = "The Excel file contains no worksheets of check voucher header.";
                            return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
                        }

                        if (worksheet2 == null)
                        {
                            TempData["error"] = "The Excel file contains no worksheets of check voucher details.";
                            return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
                        }

                        if (worksheet.ToString() != "CheckVoucherHeader")
                        {
                            TempData["error"] = "The Excel file is not related to check voucher.";
                            return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
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
                                var existingPo = await _dbContext.PurchaseOrders.FirstOrDefaultAsync(si => si.OriginalDocumentId == purchaseOrder.OriginalDocumentId, cancellationToken);

                                if (existingPo!.PurchaseOrderNo!.TrimStart().TrimEnd() != worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["PONo"] = (existingPo.PurchaseOrderNo.TrimStart().TrimEnd(), worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["Date"] = (existingPo.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.Terms.TrimStart().TrimEnd() != worksheet3.Cells[row, 2].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["Terms"] = (existingPo.Terms.TrimStart().TrimEnd(), worksheet3.Cells[row, 2].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.Quantity.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    poChanges["Quantity"] = (existingPo.Quantity.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingPo.Price.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    poChanges["Price"] = (existingPo.Price.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingPo.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    poChanges["Amount"] = (existingPo.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingPo.FinalPrice?.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    poChanges["FinalPrice"] = (existingPo.FinalPrice?.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())!;
                                }

                                if (existingPo.Remarks.TrimStart().TrimEnd() != worksheet3.Cells[row, 10].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["Remarks"] = (existingPo.Remarks.TrimStart().TrimEnd(), worksheet3.Cells[row, 10].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.CreatedBy!.TrimStart().TrimEnd() != worksheet3.Cells[row, 11].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["CreatedBy"] = (existingPo.CreatedBy.TrimStart().TrimEnd(), worksheet3.Cells[row, 11].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet3.Cells[row, 12].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["CreatedDate"] = (existingPo.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet3.Cells[row, 12].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.IsClosed.ToString().ToUpper().TrimStart().TrimEnd() != worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["IsClosed"] = (existingPo.IsClosed.ToString().ToUpper().TrimStart().TrimEnd(), worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd());
                                }

                                if ((string.IsNullOrWhiteSpace(existingPo.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingPo.CancellationRemarks.TrimStart().TrimEnd()) != worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["CancellationRemarks"] = (existingPo.CancellationRemarks?.TrimStart().TrimEnd(), worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingPo.OriginalProductId.ToString()!.TrimStart().TrimEnd() != (worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd()))
                                {
                                    poChanges["OriginalProductId"] = (existingPo.OriginalProductId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.OriginalSeriesNumber != null && existingPo.OriginalSeriesNumber.TrimStart().TrimEnd() != worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())
                                {
                                    poChanges["OriginalSeriesNumber"] = (existingPo.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.OriginalSupplierId.ToString()!.TrimStart().TrimEnd() != (worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd()))
                                {
                                    poChanges["SupplierId"] = (existingPo.SupplierId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 17].Text.TrimStart().TrimEnd());
                                }

                                if (existingPo.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd()))
                                {
                                    poChanges["OriginalDocumentId"] = (existingPo.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd());
                                }

                                if (poChanges.Any())
                                {
                                    await _purchaseOrderRepo.LogChangesAsync(existingPo.OriginalDocumentId, poChanges, _userManager.GetUserName(this.User), existingPo.PurchaseOrderNo);
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
                                var existingRr = await _dbContext.ReceivingReports.FirstOrDefaultAsync(rr => rr.OriginalDocumentId == receivingReport.OriginalDocumentId, cancellationToken);

                                if (existingRr!.ReceivingReportNo!.TrimStart().TrimEnd() != worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["RRNo"] = (existingRr.ReceivingReportNo.TrimStart().TrimEnd(), worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd());
                                }

                                if (existingRr.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["Date"] = (existingRr.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd());
                                }

                                if (existingRr.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["DueDate"] = (existingRr.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd());
                                }

                                if (existingRr.SupplierInvoiceNumber?.TrimStart().TrimEnd() != (worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd()))
                                {
                                    rrChanges["SupplierInvoiceNumber"] = (existingRr.SupplierInvoiceNumber?.TrimStart().TrimEnd(), worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 3].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.SupplierInvoiceDate?.TrimStart().TrimEnd() != worksheet4.Cells[row, 4].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["SupplierInvoiceDate"] = (existingRr.SupplierInvoiceDate?.TrimStart().TrimEnd(), worksheet4.Cells[row, 4].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.TruckOrVessels.TrimStart().TrimEnd() != worksheet4.Cells[row, 5].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["TruckOrVessels"] = (existingRr.TruckOrVessels.TrimStart().TrimEnd(), worksheet4.Cells[row, 5].Text.TrimStart().TrimEnd());
                                }

                                if (existingRr.QuantityDelivered.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    rrChanges["QuantityDelivered"] = (existingRr.QuantityDelivered.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingRr.QuantityReceived.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    rrChanges["QuantityReceived"] = (existingRr.QuantityReceived.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingRr.GainOrLoss.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    rrChanges["GainOrLoss"] = (existingRr.GainOrLoss.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingRr.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    rrChanges["Amount"] = (existingRr.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingRr.OtherRef?.TrimStart().TrimEnd() != (worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd()))
                                {
                                    rrChanges["OtherRef"] = (existingRr.OtherRef?.TrimStart().TrimEnd(), worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet4.Cells[row, 10].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.Remarks.TrimStart().TrimEnd() != worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["Remarks"] = (existingRr.Remarks.TrimStart().TrimEnd(), worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd());
                                }

                                if (existingRr.CreatedBy?.TrimStart().TrimEnd() != worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["CreatedBy"] = (existingRr.CreatedBy?.TrimStart().TrimEnd(), worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["CreatedDate"] = (existingRr.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd());
                                }

                                if ((string.IsNullOrWhiteSpace(existingRr.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingRr.CancellationRemarks.TrimStart().TrimEnd()) != worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["CancellationRemarks"] = (existingRr.CancellationRemarks?.TrimStart().TrimEnd(), worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd()))
                                {
                                    rrChanges["ReceivedDate"] = (existingRr.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.OriginalPOId?.ToString().TrimStart().TrimEnd() != (worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd()))
                                {
                                    rrChanges["OriginalPOId"] = (existingRr.OriginalPOId?.ToString().TrimStart().TrimEnd(), worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 20].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.OriginalSeriesNumber?.TrimStart().TrimEnd() != worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd())
                                {
                                    rrChanges["OriginalSeriesNumber"] = (existingRr.OriginalSeriesNumber?.TrimStart().TrimEnd(), worksheet4.Cells[row, 21].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingRr.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd()))
                                {
                                    rrChanges["OriginalDocumentId"] = (existingRr.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet4.Cells[row, 22].Text.TrimStart().TrimEnd());
                                }

                                if (rrChanges.Any())
                                {
                                    await _receivingReportRepo.LogChangesAsync(existingRr.OriginalDocumentId, rrChanges, _userManager.GetUserName(this.User), existingRr.ReceivingReportNo);
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

                        var rowCount = worksheet.Dimension.Rows;
                        var cvDictionary = new Dictionary<string, bool>();
                        var checkVoucherHeadersList = await _dbContext
                            .CheckVoucherHeaders
                            .ToListAsync(cancellationToken);

                        for (int row = 2; row <= rowCount; row++) // Assuming the first row is the header
                        {
                            var checkVoucherHeader = new CheckVoucherHeader
                            {
                                CheckVoucherHeaderNo = await _checkVoucherRepo.GenerateCVNo(cancellationToken),
                                Date = DateOnly.TryParse(worksheet.Cells[row, 1].Text, out DateOnly date)
                                    ? date
                                    : default,
                                RRNo = worksheet.Cells[row, 2].Text.Split(',').Select(rrNo => rrNo.Trim()).ToArray(),
                                SINo = worksheet.Cells[row, 3].Text.Split(',').Select(siNo => siNo.Trim()).ToArray(),
                                PONo = worksheet.Cells[row, 4].Text.Split(',').Select(poNo => poNo.Trim()).ToArray(),
                                Particulars = worksheet.Cells[row, 5].Text,
                                CheckNo = worksheet.Cells[row, 6].Text,
                                Category = worksheet.Cells[row, 7].Text,
                                Payee = worksheet.Cells[row, 8].Text,
                                CheckDate = DateOnly.TryParse(worksheet.Cells[row, 9].Text, out DateOnly checkDate)
                                    ? checkDate
                                    : default,
                                StartDate = DateOnly.TryParse(worksheet.Cells[row, 10].Text, out DateOnly startDate)
                                    ? startDate
                                    : default,
                                EndDate = DateOnly.TryParse(worksheet.Cells[row, 11].Text, out DateOnly endDate)
                                    ? endDate
                                    : default,
                                NumberOfMonths = int.TryParse(worksheet.Cells[row, 12].Text, out int numberOfMonths)
                                    ? numberOfMonths
                                    : 0,
                                NumberOfMonthsCreated =
                                    int.TryParse(worksheet.Cells[row, 13].Text, out int numberOfMonthsCreated)
                                        ? numberOfMonthsCreated
                                        : 0,
                                LastCreatedDate =
                                    DateTime.TryParse(worksheet.Cells[row, 14].Text, out DateTime lastCreatedDate)
                                        ? lastCreatedDate
                                        : default,
                                AmountPerMonth =
                                    decimal.TryParse(worksheet.Cells[row, 15].Text, out decimal amountPerMonth)
                                        ? amountPerMonth
                                        : 0,
                                IsComplete = bool.TryParse(worksheet.Cells[row, 16].Text, out bool isComplete) && isComplete,
                                AccruedType = worksheet.Cells[row, 17].Text,
                                Reference = worksheet.Cells[row, 18].Text,
                                CreatedBy = worksheet.Cells[row, 19].Text,
                                CreatedDate = DateTime.TryParse(worksheet.Cells[row, 20].Text, out DateTime createdDate)
                                    ? createdDate
                                    : default,
                                PostedBy = worksheet.Cells[row, 32].Text,
                                PostedDate = DateTime.TryParse(worksheet.Cells[row, 33].Text, out DateTime postedDate)
                                    ? postedDate
                                    : default,
                                Total = decimal.TryParse(worksheet.Cells[row, 21].Text, out decimal total) ? total : 0,
                                Amount = worksheet.Cells[row, 22].Text.Split(' ').Select(arrayAmount =>
                                    decimal.TryParse(arrayAmount.Trim(), out decimal amount) ? amount : 0).ToArray(),
                                CheckAmount = decimal.TryParse(worksheet.Cells[row, 23].Text, out decimal checkAmount)
                                    ? checkAmount
                                    : 0,
                                CvType = worksheet.Cells[row, 24].Text,
                                AmountPaid = decimal.TryParse(worksheet.Cells[row, 25].Text, out decimal amountPaid)
                                    ? amountPaid
                                    : 0,
                                IsPaid = bool.TryParse(worksheet.Cells[row, 26].Text, out bool isPaid) && isPaid,
                                CancellationRemarks = worksheet.Cells[row, 27].Text,
                                OriginalBankId = int.TryParse(worksheet.Cells[row, 28].Text, out int originalBankId)
                                    ? originalBankId
                                    : 0,
                                OriginalSeriesNumber = worksheet.Cells[row, 29].Text,
                                OriginalSupplierId =
                                    int.TryParse(worksheet.Cells[row, 30].Text, out int originalSupplierId)
                                        ? originalSupplierId
                                        : 0,
                                OriginalDocumentId =
                                    int.TryParse(worksheet.Cells[row, 31].Text, out int originalDocumentId)
                                        ? originalDocumentId
                                        : 0,
                            };

                            if (!cvDictionary.TryAdd(checkVoucherHeader.OriginalSeriesNumber, true) || checkVoucherHeader.OriginalSeriesNumber.Contains("CVNU") || checkVoucherHeader.OriginalSeriesNumber.Contains("INVU") || checkVoucherHeader.OriginalSeriesNumber.Contains("CVU"))
                            {
                                continue;
                            }

                            if (checkVoucherHeadersList.Any(cm => cm.OriginalDocumentId == checkVoucherHeader.OriginalDocumentId))
                            {
                                var cvChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                                var existingCv = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(si => si.OriginalDocumentId == checkVoucherHeader.OriginalDocumentId, cancellationToken);

                                if (existingCv!.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 1].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Date"] = (existingCv.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 1].Text.TrimStart().TrimEnd());
                                }

                                var rrNo = existingCv.RRNo != null
                                    ? string.Join(", ", existingCv.RRNo.Select(si => si.ToString()))
                                    : null;
                                if (rrNo != null && rrNo.TrimStart().TrimEnd() != worksheet.Cells[row, 2].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["RRNo"] = (string.Join(", ", existingCv.RRNo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet.Cells[row, 2].Text.TrimStart().TrimEnd());
                                }

                                var siNo = existingCv.SINo != null
                                    ? string.Join(", ", existingCv.SINo.Select(si => si.ToString()))
                                    : null;
                                if (siNo != null && siNo.TrimStart().TrimEnd() != worksheet.Cells[row, 3].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["SINo"] = (string.Join(", ", existingCv.SINo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet.Cells[row, 3].Text);
                                }

                                var poNo = existingCv.PONo != null
                                    ? string.Join(", ", existingCv.PONo.Select(si => si.ToString()))
                                    : null;
                                if (poNo != null && poNo.TrimStart().TrimEnd() != worksheet.Cells[row, 4].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["PONo"] = (string.Join(", ", existingCv.PONo!.Select(si => si.ToString().TrimStart().TrimEnd())), worksheet.Cells[row, 4].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.Particulars!.TrimStart().TrimEnd() != worksheet.Cells[row, 5].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Particulars"] = (existingCv.Particulars.TrimStart().TrimEnd(), worksheet.Cells[row, 5].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.CheckNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 6].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["CheckNo"] = (existingCv.CheckNo.TrimStart().TrimEnd(), worksheet.Cells[row, 6].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.Category != worksheet.Cells[row, 7].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Category"] = (existingCv.Category.TrimStart().TrimEnd(), worksheet.Cells[row, 7].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.Payee!.TrimStart().TrimEnd() != worksheet.Cells[row, 8].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["Payee"] = (existingCv.Payee.TrimStart().TrimEnd(), worksheet.Cells[row, 8].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.CheckDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet.Cells[row, 9].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 9].Text.TrimStart().TrimEnd()))
                                {
                                    cvChanges["CheckDate"] = (existingCv.CheckDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 9].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 9].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingCv.StartDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd") : worksheet.Cells[row, 10].Text).TrimStart().TrimEnd())
                                {
                                    cvChanges["StartDate"] = (existingCv.StartDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd") : worksheet.Cells[row, 10].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingCv.EndDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet.Cells[row, 11].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd") : worksheet.Cells[row, 11].Text.TrimStart().TrimEnd()))
                                {
                                    cvChanges["EndDate"] = (existingCv.EndDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 11].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 11].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingCv.NumberOfMonths.ToString().TrimStart().TrimEnd() != worksheet.Cells[row, 12].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["NumberOfMonths"] = (existingCv.NumberOfMonths.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 12].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.NumberOfMonthsCreated.ToString().TrimStart().TrimEnd() != worksheet.Cells[row, 13].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["NumberOfMonthsCreated"] = (existingCv.NumberOfMonthsCreated.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 13].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.LastCreatedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet.Cells[row, 14].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 14].Text.TrimStart().TrimEnd()))
                                {
                                    cvChanges["LastCreatedDate"] = (existingCv.LastCreatedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 14].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 14].Text.TrimStart().TrimEnd())!;
                                }

                                if (existingCv.AmountPerMonth.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvChanges["AmountPerMonth"] = (existingCv.AmountPerMonth.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCv.IsComplete.ToString().ToUpper().TrimStart().TrimEnd() != worksheet.Cells[row, 16].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["IsComplete"] = (existingCv.IsComplete.ToString().ToUpper().TrimStart().TrimEnd(), worksheet.Cells[row, 16].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.AccruedType!.TrimStart().TrimEnd() != worksheet.Cells[row, 17].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["AccruedType"] = (existingCv.AccruedType.TrimStart().TrimEnd(), worksheet.Cells[row, 17].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.CvType!.TrimStart().TrimEnd() == "Payment")
                                {
                                    var getCvInvoicing = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cvh => existingCv.Reference == cvh.CheckVoucherHeaderNo, cancellationToken);
                                    if (getCvInvoicing != null && getCvInvoicing.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet.Cells[row, 18].Text.TrimStart().TrimEnd())
                                    {
                                        cvChanges["Reference"] = (getCvInvoicing.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet.Cells[row, 18].Text.TrimStart().TrimEnd());
                                    }
                                }

                                if (existingCv.CreatedBy!.TrimStart().TrimEnd() != worksheet.Cells[row, 19].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["CreatedBy"] = (existingCv.CreatedBy.TrimStart().TrimEnd(), worksheet.Cells[row, 19].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet.Cells[row, 20].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["CreatedDate"] = (existingCv.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet.Cells[row, 20].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.Total.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 21].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvChanges["Total"] = (existingCv.Total.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 21].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCv.Category.TrimStart().TrimEnd() == "Trade")
                                {
                                    var cellText = worksheet.Cells[row, 22].Text.TrimStart().TrimEnd();
                                    if (decimal.TryParse(cellText, out var parsedAmount))
                                    {
                                        var amount = existingCv.Amount != null
                                            ? string.Join(" ", existingCv.Amount.Select(si => si.ToString("F2")))
                                            : null;

                                        if (amount != null && amount != parsedAmount.ToString("F2"))
                                        {
                                            cvChanges["Amount"] = (amount, parsedAmount.ToString("F2"));
                                        }
                                    }
                                }

                                if (existingCv.CheckAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 23].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvChanges["CheckAmount"] = (existingCv.CheckAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 23].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCv.CvType.TrimStart().TrimEnd() != worksheet.Cells[row, 24].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["CvType"] = (existingCv.CvType.TrimStart().TrimEnd(), worksheet.Cells[row, 24].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.AmountPaid.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 25].Text).ToString("F2").TrimStart().TrimEnd())
                                {
                                    cvChanges["AmountPaid"] = (existingCv.AmountPaid.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 25].Text).ToString("F2").TrimStart().TrimEnd());
                                }

                                if (existingCv.IsPaid.ToString().ToUpper().TrimStart().TrimEnd() != worksheet.Cells[row, 26].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["IsPaid"] = (existingCv.IsPaid.ToString().ToUpper().TrimStart().TrimEnd(), worksheet.Cells[row, 26].Text.TrimStart().TrimEnd());
                                }

                                if ((string.IsNullOrWhiteSpace(existingCv.CancellationRemarks) ? "" : existingCv.CancellationRemarks.TrimStart().TrimEnd()) != worksheet.Cells[row, 27].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["CancellationRemarks"] = (existingCv.CancellationRemarks!.TrimStart().TrimEnd(), worksheet.Cells[row, 27].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.OriginalBankId.ToString()!.TrimStart().TrimEnd() != (worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() != "" ? worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() : 0.ToString()))
                                {
                                    cvChanges["OriginalBankId"] = (existingCv.OriginalBankId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() != "" ? worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() : 0.ToString());
                                }

                                if (existingCv.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet.Cells[row, 29].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["OriginalSeriesNumber"] = (existingCv.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet.Cells[row, 29].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.OriginalSupplierId.ToString()!.TrimStart().TrimEnd() != worksheet.Cells[row, 30].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["OriginalSupplierId"] = (existingCv.OriginalSupplierId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 30].Text.TrimStart().TrimEnd());
                                }

                                if (existingCv.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet.Cells[row, 31].Text.TrimStart().TrimEnd())
                                {
                                    cvChanges["OriginalDocumentId"] = (existingCv.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 31].Text.TrimStart().TrimEnd());
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

                            if (checkVoucherHeader.CvType == "Payment")
                            {
                                var getCvInvoicing = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cvh => cvh.OriginalSeriesNumber == checkVoucherHeader.Reference, cancellationToken);
                                checkVoucherHeader.Reference = getCvInvoicing?.CheckVoucherHeaderNo;
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

                        #region -- Check Voucher Trade Payment Import --

                        var cvTradePaymentRowCount = worksheet5!.Dimension.Rows;

                        for (int cvTradePaymentRow = 2; cvTradePaymentRow <= cvTradePaymentRowCount; cvTradePaymentRow++)
                        {
                            var rrId = int.TryParse(worksheet5.Cells[cvTradePaymentRow, 2].Text, out int rrDocumentId)
                                ? rrDocumentId
                                : 0;
                            var cvId = int.TryParse(worksheet5.Cells[cvTradePaymentRow, 4].Text, out int cvDocumentId)
                                ? cvDocumentId
                                : 0;
                            var getRr = await _dbContext.ReceivingReports.FirstOrDefaultAsync(rr => rr.OriginalDocumentId == rrId, cancellationToken: cancellationToken);
                            var getCv = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cv => cv.OriginalDocumentId == cvId, cancellationToken: cancellationToken);
                            var cvTradePayment = new CVTradePayment
                            {
                                DocumentId = getRr!.ReceivingReportId,
                                DocumentType = "RR",
                                CheckVoucherId = getCv!.CheckVoucherHeaderId,
                                AmountPaid = decimal.TryParse(worksheet5.Cells[cvTradePaymentRow, 5].Text, out decimal amountPaid) ? amountPaid : 0,
                            };

                            if (!checkVoucherHeadersList.Select(cv => cv.OriginalDocumentId).Contains(cvId))
                            {
                                await _dbContext.CVTradePayments.AddAsync(cvTradePayment, cancellationToken);
                            }
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);

                        #endregion -- Check Voucher Trade Payment Import --

                        #region -- Check Voucher Multiple Payment Import --

                        var cvMultiplePaymentRowCount = worksheet6!.Dimension.Rows;
                        var cvMultiplePaymentList = await _dbContext
                            .MultipleCheckVoucherPayments
                            .Include(cvmp => cvmp.CheckVoucherHeaderPayment)
                            .Include(cvmp => cvmp.CheckVoucherHeaderInvoice)
                            .ToListAsync(cancellationToken);

                        for (int cvMultiplePaymentRow = 2; cvMultiplePaymentRow <= cvMultiplePaymentRowCount; cvMultiplePaymentRow++)
                        {
                            var paymentId = int.TryParse(worksheet6.Cells[cvMultiplePaymentRow, 2].Text, out int cvnId)
                                ? cvnId
                                : 0;
                            var invoiceId = int.TryParse(worksheet6.Cells[cvMultiplePaymentRow, 3].Text, out int invId)
                                ? invId
                                : 0;
                            var getPayment = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(rr => rr.OriginalDocumentId == paymentId, cancellationToken: cancellationToken);
                            var getInvoice = await _dbContext.CheckVoucherHeaders.FirstOrDefaultAsync(cv => cv.OriginalDocumentId == invoiceId, cancellationToken: cancellationToken);

                            if (getInvoice != null && getPayment != null)
                            {
                                var cvMultiplePayment = new MultipleCheckVoucherPayment
                                {
                                    Id = Guid.NewGuid(),
                                    CheckVoucherHeaderPaymentId = getPayment.CheckVoucherHeaderId, // Guaranteed non-null
                                    CheckVoucherHeaderInvoiceId = getInvoice.CheckVoucherHeaderId, // Guaranteed non-null
                                    AmountPaid = decimal.TryParse(worksheet6.Cells[cvMultiplePaymentRow, 4]?.Text,
                                        out decimal amountPaid)
                                        ? amountPaid
                                        : 0,
                                };

                                if (!cvMultiplePaymentList.Select(cv => cv.CheckVoucherHeaderPayment!.OriginalDocumentId).Contains(paymentId))
                                {
                                    await _dbContext.MultipleCheckVoucherPayments.AddAsync(cvMultiplePayment, cancellationToken);
                                }
                            }
                        }

                        await _dbContext.SaveChangesAsync(cancellationToken);

                        #endregion -- Check Voucher Multiple Payment Import --

                        #region -- Check Voucher Details Import --

                        var cvdRowCount = worksheet2.Dimension.Rows;
                        var checkVoucherDetailList = await _dbContext
                            .CheckVoucherDetails
                            .ToListAsync(cancellationToken);

                        for (var cvdRow = 2; cvdRow <= cvdRowCount; cvdRow++)
                        {
                            var cvRow = cvdRow;

                            var checkVoucherDetails = new CheckVoucherDetail
                            {
                                AccountNo = worksheet2.Cells[cvdRow, 1].Text,
                                AccountName = worksheet2.Cells[cvdRow, 2].Text,
                                Debit = decimal.TryParse(worksheet2.Cells[cvdRow, 4].Text, out decimal debit) ? debit : 0,
                                Credit = decimal.TryParse(worksheet2.Cells[cvdRow, 5].Text, out decimal credit) ? credit : 0,
                                OriginalDocumentId = int.TryParse(worksheet2.Cells[cvdRow, 7].Text, out int originalDocumentId) ? originalDocumentId : 0,
                                Amount = decimal.TryParse(worksheet2.Cells[cvdRow, 8].Text, out decimal amount) ? amount : 0,
                                AmountPaid = decimal.TryParse(worksheet2.Cells[cvdRow, 9].Text, out decimal amountPaid) ? amountPaid : 0,
                                EwtPercent = decimal.TryParse(worksheet2.Cells[cvdRow, 11].Text, out decimal ewtPercent) ? ewtPercent : 0,
                                IsUserSelected = bool.TryParse(worksheet2.Cells[cvdRow, 12].Text, out bool isUserSelected) && isUserSelected,
                                IsVatable = bool.TryParse(worksheet2.Cells[cvdRow, 13].Text, out bool isVatable) && isVatable
                            };

                            var cvHeader = await _dbContext.CheckVoucherHeaders
                                .Where(cvh => cvh.OriginalDocumentId.ToString() == worksheet2.Cells[cvRow, 6].Text.TrimStart().TrimEnd())
                                .FirstOrDefaultAsync(cancellationToken);

                            if (cvHeader != null)
                            {
                                var getSupplier = await _dbContext.Suppliers
                                    .Where(cvh => cvh.OriginalSupplierId.ToString() == worksheet2.Cells[cvRow, 10].Text.TrimStart().TrimEnd())
                                    .FirstOrDefaultAsync(cancellationToken);

                                checkVoucherDetails.SupplierId = getSupplier?.SupplierId ?? null;
                                checkVoucherDetails.CheckVoucherHeaderId = cvHeader.CheckVoucherHeaderId;
                                checkVoucherDetails.TransactionNo = cvHeader.CheckVoucherHeaderNo!;
                            }

                            if (!checkVoucherDetailList.Any(cm => cm.OriginalDocumentId == checkVoucherDetails.OriginalDocumentId) && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("CVNU") && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("INVU") && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("CVU"))
                            {
                                await _dbContext.CheckVoucherDetails.AddAsync(checkVoucherDetails, cancellationToken);
                            }

                            if (checkVoucherDetailList.Any(cm => cm.OriginalDocumentId == checkVoucherDetails.OriginalDocumentId) && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("CVNU") && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("INVU") && !worksheet2.Cells[cvdRow, 3].Text.TrimStart().TrimEnd().Contains("CVU"))
                            {
                                var cvdChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                                var existingCvd = await _dbContext.CheckVoucherDetails
                                    .Include(cvd => cvd.CheckVoucherHeader)
                                    .FirstOrDefaultAsync(cvd => cvd.OriginalDocumentId == checkVoucherDetails.OriginalDocumentId, cancellationToken);

                                if (existingCvd != null)
                                {
                                    if (existingCvd.AccountNo.TrimStart().TrimEnd() != worksheet2.Cells[cvdRow, 1].Text.TrimStart().TrimEnd())
                                    {
                                        cvdChanges["AccountNo"] = (existingCvd.AccountNo.TrimStart().TrimEnd(), worksheet2.Cells[cvdRow, 1].Text.TrimStart().TrimEnd());
                                    }

                                    if (existingCvd.AccountName.TrimStart().TrimEnd() != worksheet2.Cells[cvdRow, 2].Text.TrimStart().TrimEnd())
                                    {
                                        cvdChanges["AccountName"] = (existingCvd.AccountName.TrimStart().TrimEnd(), worksheet2.Cells[cvdRow, 2].Text.TrimStart().TrimEnd());
                                    }

                                    if (existingCvd.Debit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[cvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd())
                                    {
                                        cvdChanges["Debit"] = (existingCvd.Debit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[cvdRow, 4].Text).ToString("F2").TrimStart().TrimEnd());
                                    }

                                    if (existingCvd.Credit.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[cvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd())
                                    {
                                        cvdChanges["Credit"] = (existingCvd.Credit.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[cvdRow, 5].Text).ToString("F2").TrimStart().TrimEnd());
                                    }

                                    if (existingCvd.CheckVoucherHeader?.OriginalDocumentId.ToString().TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[cvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd())
                                    {
                                        cvdChanges["CVHeaderId"] = (existingCvd.CheckVoucherHeader?.OriginalDocumentId.ToString().TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[cvdRow, 6].Text).ToString("F0").TrimStart().TrimEnd())!;
                                    }

                                    if (cvdChanges.Any())
                                    {
                                        await _checkVoucherRepo.LogChangesForCVDAsync(existingCvd.OriginalDocumentId, cvdChanges, _userManager.GetUserName(this.User), existingCvd.TransactionNo);
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

                        #endregion -- Check Voucher Details Import --

                        TempData["success"] = "Uploading Success!";
                    }
                    else
                    {
                        TempData["warning"] = "The Uploaded Excel file is not related to AAS.";
                    }
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
                }
                catch (InvalidOperationException ioe)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["warning"] = ioe.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
                }
            }

            return RedirectToAction(nameof(Index), new { view = DynamicView.CheckVoucher });
        }

        #endregion -- import xlsx record --
    }
}
