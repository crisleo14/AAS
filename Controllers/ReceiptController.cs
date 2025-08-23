using System.Globalization;
using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Models.Reports;
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
    public class ReceiptController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly ReceiptRepo _receiptRepo;

        private readonly SalesInvoiceRepo _salesInvoiceRepo;

        private readonly ServiceInvoiceRepo _serviceInvoiceRepo;

        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly GeneralRepo _generalRepo;

        public ReceiptController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, ReceiptRepo receiptRepo, IWebHostEnvironment webHostEnvironment, GeneralRepo generalRepo, SalesInvoiceRepo salesInvoiceRepo, ServiceInvoiceRepo serviceInvoiceRepo)
        {
            _dbContext = dbContext;
            this._userManager = userManager;
            _receiptRepo = receiptRepo;
            _webHostEnvironment = webHostEnvironment;
            _generalRepo = generalRepo;
            _salesInvoiceRepo = salesInvoiceRepo;
            _serviceInvoiceRepo = serviceInvoiceRepo;
        }

        public async Task<IActionResult> CollectionIndex(string? view, CancellationToken cancellationToken)
        {
            var collectionReceipts = await _receiptRepo.GetCollectionReceiptsAsync(cancellationToken);

            if (view == nameof(DynamicView.CollectionReceipt))
            {
                return View("ImportExportIndex", collectionReceipts);
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetCollectionReceipts([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var collectionReceipts = await _receiptRepo.GetCollectionReceiptsAsync(cancellationToken);
                // Search filter
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();
                    collectionReceipts = collectionReceipts
                        .Where(cr =>
                            cr.CollectionReceiptNo!.ToLower().Contains(searchValue) ||
                            cr.TransactionDate.ToString("MMM dd, yyyy").ToLower().Contains(searchValue) ||
                            cr.SINo?.ToLower().Contains(searchValue) == true ||
                            cr.SVNo?.ToLower().Contains(searchValue) == true ||
                            cr.MultipleSI?.Contains(searchValue) == true ||
                            cr.Customer!.CustomerName.ToLower().Contains(searchValue) ||
                            cr.Total.ToString(CultureInfo.InvariantCulture).ToLower().Contains(searchValue) ||
                            cr.CreatedBy!.ToLower().Contains(searchValue)
                            )
                        .ToList();
                }
                // Sorting
                if (parameters.Order != null && parameters.Order.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";
                    collectionReceipts = collectionReceipts
                        .AsQueryable()
                        .OrderBy($"{columnName} {sortDirection}")
                        .ToList();
                }
                var totalRecords = collectionReceipts.Count();
                var pagedData = collectionReceipts
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
                return RedirectToAction(nameof(CollectionIndex));
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetAllCollectionReceiptIds(CancellationToken cancellationToken)
        {
            var collectionReceiptIds = await _dbContext.CollectionReceipts
                                     .Select(cr => cr.CollectionReceiptId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(collectionReceiptIds);
        }

        [HttpGet]
        public async Task<IActionResult> SingleCollectionCreateForSales(CancellationToken cancellationToken)
        {
            var viewModel = new CollectionReceipt
            {
                Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.CustomerId.ToString(),
                        Text = s.CustomerName
                    })
                    .ToListAsync(cancellationToken),
                ChartOfAccounts = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> SingleCollectionCreateForSales(CollectionReceipt model, string[] accountTitleText, decimal[] accountAmount, string[] accountTitle, IFormFile? bir2306, IFormFile? bir2307, CancellationToken cancellationToken)
        {
            model.Customers = await _dbContext.Customers
               .OrderBy(c => c.CustomerId)
               .Select(s => new SelectListItem
               {
                   Value = s.Number.ToString(),
                   Text = s.CustomerName
               })
               .ToListAsync(cancellationToken);

            model.SalesInvoices = await _dbContext.SalesInvoices
                .Where(si => !si.IsPaid && si.CustomerId == model.CustomerId && si.IsPosted)
                .OrderBy(si => si.SalesInvoiceId)
                .Select(s => new SelectListItem
                {
                    Value = s.SalesInvoiceId.ToString(),
                    Text = s.SalesInvoiceNo
                })
                .ToListAsync(cancellationToken);

            model.ChartOfAccounts = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Validating the series

                    var generateCrNo = await _receiptRepo.GenerateCRNo(cancellationToken);
                    var getLastNumber = long.Parse(generateCrNo.Substring(2));

                    if (getLastNumber > 9999999999)
                    {
                        TempData["error"] = "You reach the maximum Series Number";
                        return View(model);
                    }
                    var totalRemainingSeries = 9999999999 - getLastNumber;
                    if (getLastNumber >= 9999999899)
                    {
                        TempData["warning"] = $"Collection Receipt created successfully, Warning {totalRemainingSeries} series number remaining";
                    }
                    else
                    {
                        TempData["success"] = "Collection Receipt created successfully";
                    }

                    #endregion --Validating the series

                    #region --Saving default value

                    var computeTotalInModelIfZero = model.CashAmount + model.CheckAmount + model.ManagerCheckAmount + model.EWT + model.WVAT;
                    if (computeTotalInModelIfZero == 0)
                    {
                        TempData["error"] = "Please input atleast one type form of payment";
                        return View(model);
                    }
                    var existingSalesInvoice = await _dbContext.SalesInvoices
                                                   .FirstOrDefaultAsync(si => si.SalesInvoiceId == model.SalesInvoiceId, cancellationToken);

                    model.SINo = existingSalesInvoice!.SalesInvoiceNo;
                    model.CollectionReceiptNo = generateCrNo;
                    model.CreatedBy = User.Identity!.Name;
                    model.Total = computeTotalInModelIfZero;

                        if (bir2306 != null && bir2306.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2306");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2306.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2306.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2306FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                        if (bir2307 != null && bir2307.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2307");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2307.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2307.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2307FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                    await _dbContext.AddAsync(model, cancellationToken);

                    #endregion --Saving default value

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.CreatedBy!, $"Create new collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    #region --Offsetting function

                    var offsettings = new List<Offsetting>();

                    for (int i = 0; i < accountTitle.Length; i++)
                    {
                        var currentAccountTitle = accountTitleText[i];
                        var currentAccountAmount = accountAmount[i];

                        var splitAccountTitle = currentAccountTitle.Split([' '], 2);

                        offsettings.Add(
                            new Offsetting
                            {
                                AccountNo = accountTitle[i],
                                AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0],
                                Source = model.CollectionReceiptNo,
                                Reference = model.SINo,
                                Amount = currentAccountAmount,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }

                    await _dbContext.AddRangeAsync(offsettings, cancellationToken);

                    #endregion --Offsetting function

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(CollectionIndex));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(CollectionIndex));
                }
            }
            else
            {
                TempData["error"] = "The information you submitted is not valid!";
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> MultipleCollectionCreateForSales(CancellationToken cancellationToken)
        {
            var viewModel = new CollectionReceipt
            {
                Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.CustomerId.ToString(),
                        Text = s.CustomerName
                    })
                    .ToListAsync(cancellationToken),
                ChartOfAccounts = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> MultipleCollectionCreateForSales(CollectionReceipt model, string[] accountTitleText, decimal[] accountAmount, string[] accountTitle, IFormFile? bir2306, IFormFile? bir2307, CancellationToken cancellationToken)
        {
            model.Customers = await _dbContext.Customers
               .OrderBy(c => c.CustomerId)
               .Select(s => new SelectListItem
               {
                   Value = s.Number.ToString(),
                   Text = s.CustomerName
               })
               .ToListAsync(cancellationToken);

            model.SalesInvoices = await _dbContext.SalesInvoices
                .Where(si => !si.IsPaid && si.CustomerId == model.CustomerId && si.IsPosted)
                .OrderBy(si => si.SalesInvoiceId)
                .Select(s => new SelectListItem
                {
                    Value = s.SalesInvoiceId.ToString(),
                    Text = s.SalesInvoiceNo
                })
                .ToListAsync(cancellationToken);

            model.ChartOfAccounts = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    #region --Validating the series

                    var generateCrNo = await _receiptRepo.GenerateCRNo(cancellationToken);
                    var getLastNumber = long.Parse(generateCrNo.Substring(2));

                    if (getLastNumber > 9999999999)
                    {
                        TempData["error"] = "You reach the maximum Series Number";
                        return View(model);
                    }
                    var totalRemainingSeries = 9999999999 - getLastNumber;
                    if (getLastNumber >= 9999999899)
                    {
                        TempData["warning"] = $"Collection Receipt created successfully, Warning {totalRemainingSeries} series number remaining";
                    }
                    else
                    {
                        TempData["success"] = "Collection Receipt created successfully";
                    }

                    #endregion --Validating the series

                    #region --Saving default value

                    var computeTotalInModelIfZero = model.CashAmount + model.CheckAmount + model.ManagerCheckAmount + model.EWT + model.WVAT;
                    if (computeTotalInModelIfZero == 0)
                    {
                        TempData["error"] = "Please input atleast one type form of payment";
                        return View(model);
                    }

                    model.MultipleSI = new string[model.MultipleSIId!.Length];
                    model.MultipleTransactionDate = new DateOnly[model.MultipleSIId.Length];
                    for (int i = 0; i < model.MultipleSIId.Length; i++)
                    {
                        var siId = model.MultipleSIId[i];
                        var salesInvoice = await _dbContext.SalesInvoices
                            .FirstOrDefaultAsync(si => si.SalesInvoiceId == siId, cancellationToken);

                        if (salesInvoice != null)
                        {
                            model.MultipleSI[i] = salesInvoice.SalesInvoiceNo!;
                            model.MultipleTransactionDate[i] = salesInvoice.TransactionDate;
                        }
                    }

                    model.CollectionReceiptNo = generateCrNo;
                    model.CreatedBy = _userManager.GetUserName(this.User);
                    model.Total = computeTotalInModelIfZero;

                        if (bir2306 != null && bir2306.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2306");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2306.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2306.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2306FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                        if (bir2307 != null && bir2307.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2307");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            var fileName = Path.GetFileName(bir2307.FileName);
                            var fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2307.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2307FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                    await _dbContext.AddAsync(model, cancellationToken);

                    #endregion --Saving default value

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.CreatedBy!, $"Create new collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    #region --Offsetting function

                    var offsettings = new List<Offsetting>();

                    for (int i = 0; i < accountTitle.Length; i++)
                    {
                        var currentAccountTitle = accountTitleText[i];
                        var currentAccountAmount = accountAmount[i];

                        var splitAccountTitle = currentAccountTitle.Split([' '], 2);

                        offsettings.Add(
                            new Offsetting
                            {
                                AccountNo = accountTitle[i],
                                AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0],
                                Source = model.CollectionReceiptNo,
                                Reference = model.SINo,
                                Amount = currentAccountAmount,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }

                    await _dbContext.AddRangeAsync(offsettings, cancellationToken);

                    #endregion --Offsetting function

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(CollectionIndex));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(CollectionIndex));
                }
            }
            else
            {
                TempData["error"] = "The information you submitted is not valid!";
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> CollectionCreateForService(CancellationToken cancellationToken)
        {
            var viewModel = new CollectionReceipt
            {
                Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.CustomerId.ToString(),
                        Text = s.CustomerName
                    })
                    .ToListAsync(cancellationToken),
                ChartOfAccounts = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> CollectionCreateForService(CollectionReceipt model, string[] accountTitleText, decimal[] accountAmount, string[] accountTitle, IFormFile? bir2306, IFormFile? bir2307, CancellationToken cancellationToken)
        {
            model.Customers = await _dbContext.Customers
               .OrderBy(c => c.CustomerId)
               .Select(s => new SelectListItem
               {
                   Value = s.CustomerId.ToString(),
                   Text = s.CustomerName
               })
               .ToListAsync(cancellationToken);

            model.SalesInvoices = await _dbContext.ServiceInvoices
                .Where(si => !si.IsPaid && si.CustomerId == model.CustomerId && si.IsPosted)
                .OrderBy(si => si.ServiceInvoiceId)
                .Select(s => new SelectListItem
                {
                    Value = s.ServiceInvoiceId.ToString(),
                    Text = s.ServiceInvoiceNo
                })
                .ToListAsync(cancellationToken);

            model.ChartOfAccounts = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Validating the series

                    var generateCrNo = await _receiptRepo.GenerateCRNo(cancellationToken);
                    var getLastNumber = long.Parse(generateCrNo.Substring(2));

                    if (getLastNumber > 9999999999)
                    {
                        TempData["error"] = "You reach the maximum Series Number";
                        return View(model);
                    }
                    var totalRemainingSeries = 9999999999 - getLastNumber;
                    if (getLastNumber >= 9999999899)
                    {
                        TempData["warning"] = $"Collection Receipt created successfully, Warning {totalRemainingSeries} series number remaining";
                    }
                    else
                    {
                        TempData["success"] = "Collection Receipt created successfully";
                    }

                    #endregion --Validating the series

                    #region --Saving default value

                    var computeTotalInModelIfZero = model.CashAmount + model.CheckAmount + model.ManagerCheckAmount + model.EWT + model.WVAT;
                    if (computeTotalInModelIfZero == 0)
                    {
                        TempData["error"] = "Please input atleast one type form of payment";
                        return View(model);
                    }
                    var existingServiceInvoice = await _dbContext.ServiceInvoices
                                                   .FirstOrDefaultAsync(si => si.ServiceInvoiceId == model.ServiceInvoiceId, cancellationToken);

                    model.SVNo = existingServiceInvoice!.ServiceInvoiceNo;
                    model.CollectionReceiptNo = generateCrNo;
                    model.CreatedBy = User.Identity!.Name;
                    model.Total = computeTotalInModelIfZero;

                        if (bir2306 != null && bir2306.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2306");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2306.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2306.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2306FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                        if (bir2307 != null && bir2307.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2307");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2307.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2307.CopyToAsync(stream, cancellationToken);
                            }

                            model.F2307FilePath = fileSavePath;
                            model.IsCertificateUpload = true;
                        }

                    await _dbContext.AddAsync(model, cancellationToken);

                    #endregion --Saving default value

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.CreatedBy!, $"Create new collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    #region --Offsetting function

                    var offsettings = new List<Offsetting>();

                    for (int i = 0; i < accountTitle.Length; i++)
                    {
                        var currentAccountTitle = accountTitleText[i];
                        var currentAccountAmount = accountAmount[i];

                        var splitAccountTitle = currentAccountTitle.Split([' '], 2);

                        offsettings.Add(
                            new Offsetting
                            {
                                AccountNo = accountTitle[i],
                                AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0],
                                Source = model.CollectionReceiptNo,
                                Reference = model.SVNo,
                                Amount = currentAccountAmount,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }

                    await _dbContext.AddRangeAsync(offsettings, cancellationToken);

                    #endregion --Offsetting function

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(CollectionIndex));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(CollectionIndex));
                }
            }
            else
            {
                TempData["error"] = "The information you submitted is not valid!";
                return View(model);
            }
        }

        public async Task<IActionResult> CollectionPrint(int id, CancellationToken cancellationToken)
        {
            var cr = await _receiptRepo.FindCR(id, cancellationToken);
            return View(cr);
        }
        public async Task<IActionResult> MultipleCollectionPrint(int id, CancellationToken cancellationToken)
        {
            var cr = await _receiptRepo.FindCR(id, cancellationToken);
            return View(cr);
        }

        public async Task<IActionResult> PrintedCollectionReceipt(int id, CancellationToken cancellationToken)
        {
            var findIdOfCr = await _receiptRepo.FindCR(id, cancellationToken);
            if (!findIdOfCr.IsPrinted)
            {

                #region --Audit Trail Recording

                if (findIdOfCr.OriginalSeriesNumber.IsNullOrEmpty() && findIdOfCr.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var printedBy = _userManager.GetUserName(this.User);
                    AuditTrail auditTrailBook = new(printedBy!, $"Printed original copy of cr# {findIdOfCr.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                findIdOfCr.IsPrinted = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(CollectionPrint), new { id });
        }
        public async Task<IActionResult> PrintedMultipleCR(int id, CancellationToken cancellationToken)
        {
            var findIdOfCr = await _receiptRepo.FindCR(id, cancellationToken);
            if (!findIdOfCr.IsPrinted)
            {

                #region --Audit Trail Recording

                if (findIdOfCr.OriginalSeriesNumber.IsNullOrEmpty() && findIdOfCr.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var printedBy = _userManager.GetUserName(this.User);
                    AuditTrail auditTrailBook = new(printedBy!, $"Printed original copy of cr# {findIdOfCr.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                findIdOfCr.IsPrinted = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(MultipleCollectionPrint), new { id });
        }

        [HttpGet]
        public async Task<IActionResult> GetSalesInvoices(int customerNo, CancellationToken cancellationToken)
        {
            var invoices = await _dbContext
                .SalesInvoices
                .Where(si => si.CustomerId == customerNo && !si.IsPaid && si.IsPosted)
                .OrderBy(si => si.SalesInvoiceId)
                .ToListAsync(cancellationToken);

            var invoiceList = invoices.Select(si => new SelectListItem
            {
                Value = si.SalesInvoiceId.ToString(),   // Replace with your actual ID property
                Text = si.SalesInvoiceNo              // Replace with your actual property for display text
            }).ToList();

            return Json(invoiceList);
        }

        [HttpGet]
        public async Task<IActionResult> GetServiceInvoices(int customerNo, CancellationToken cancellationToken)
        {
            var invoices = await _dbContext
                .ServiceInvoices
                .Where(si => si.CustomerId == customerNo && !si.IsPaid && si.IsPosted)
                .OrderBy(si => si.ServiceInvoiceId)
                .ToListAsync(cancellationToken);

            var invoiceList = invoices.Select(si => new SelectListItem
            {
                Value = si.ServiceInvoiceId.ToString(),   // Replace with your actual ID property
                Text = si.ServiceInvoiceNo              // Replace with your actual property for display text
            }).ToList();

            return Json(invoiceList);
        }

        [HttpGet]
        public async Task<IActionResult> GetInvoiceDetails(int invoiceNo, bool isSales, bool isServices, CancellationToken cancellationToken)
        {
            if (isSales && !isServices)
            {
                var si = await _dbContext
                .SalesInvoices
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == invoiceNo, cancellationToken);

                var netDiscount = si!.Amount - si.Discount;
                var netOfVatAmount = si.Customer!.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeNetOfVat(netDiscount) : netDiscount;
                var withHoldingTaxAmount = si.Customer.WithHoldingTax ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m) : 0;
                var withHoldingVatAmount = si.Customer.WithHoldingVat ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.05m) : 0;

                return Json(new
                {
                    Amount = netDiscount.ToString("N2"),
                    AmountPaid = si.AmountPaid.ToString("N2"),
                    Balance = si.Balance.ToString("N2"),
                    Ewt = withHoldingTaxAmount.ToString("N2"),
                    Wvat = withHoldingVatAmount.ToString("N2"),
                    Total = (netDiscount - (withHoldingTaxAmount + withHoldingVatAmount)).ToString("N2")
                });
            }
            else if (isServices && !isSales)
            {
                var sv = await _dbContext
                .ServiceInvoices
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(si => si.ServiceInvoiceId == invoiceNo, cancellationToken);

                var netOfVatAmount = sv!.Customer!.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeNetOfVat(sv.Amount) - sv.Discount : sv.Amount - sv.Discount;
                var withHoldingTaxAmount = sv.Customer.WithHoldingTax ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m) : 0;
                var withHoldingVatAmount = sv.Customer.WithHoldingVat ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.05m) : 0;

                return Json(new
                {
                    Amount = sv.Total.ToString("N2"),
                    AmountPaid = sv.AmountPaid.ToString("N2"),
                    Balance = sv.Balance.ToString("N2"),
                    Ewt = withHoldingTaxAmount.ToString("N2"),
                    Wvat = withHoldingVatAmount.ToString("N2"),
                    Total = (sv.Total - (withHoldingTaxAmount + withHoldingVatAmount)).ToString("N2")
                });
            }
            return Json(null);
        }

        public async Task<IActionResult> MultipleInvoiceBalance(int siNo, CancellationToken cancellationToken)
        {
            var salesInvoice = await _dbContext.SalesInvoices
                .Include(c => c.Customer)
                .FirstOrDefaultAsync(si => si.SalesInvoiceId == siNo, cancellationToken);
            if (salesInvoice != null)
            {
                var amount = salesInvoice.Amount;
                var amountPaid = salesInvoice.AmountPaid;
                var netAmount = salesInvoice.Amount - salesInvoice.Discount;
                var vatAmount = salesInvoice.Customer!.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeVatAmount((netAmount / 1.12m) * 0.12m) : 0;
                var ewtAmount = salesInvoice.Customer.WithHoldingTax ? _generalRepo.ComputeEwtAmount((netAmount / 1.12m), 0.01m) : 0;
                var wvatAmount = salesInvoice.Customer.WithHoldingVat ? _generalRepo.ComputeEwtAmount((netAmount / 1.12m), 0.05m) : 0;
                var balance = amount - amountPaid;

                return Json(new
                {
                    Amount = amount,
                    AmountPaid = amountPaid,
                    NetAmount = netAmount,
                    VatAmount = vatAmount,
                    EwtAmount = ewtAmount,
                    WvatAmount = wvatAmount,
                    Balance = balance
                });
            }
            return Json(null);
        }

        [HttpGet]
        public async Task<IActionResult> GetMultipleInvoiceDetails(int[] siNo, bool isSales, CancellationToken cancellationToken)
        {
            if (isSales)
            {
                var si = await _dbContext
                .SalesInvoices
                .Include(x => x.Customer)
                .FirstOrDefaultAsync(si => siNo.Contains(si.SalesInvoiceId), cancellationToken);

                var netDiscount = si!.Amount - si.Discount;
                var netOfVatAmount = si.Customer!.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeNetOfVat(netDiscount) : netDiscount;
                var withHoldingTaxAmount = si.Customer.WithHoldingTax ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m) : 0;
                var withHoldingVatAmount = si.Customer.WithHoldingVat ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.05m) : 0;

                return Json(new
                {
                    Amount = netDiscount,
                    si.AmountPaid,
                    si.Balance,
                    WithholdingTax = withHoldingTaxAmount,
                    WithholdingVat = withHoldingVatAmount,
                    Total = netDiscount - (withHoldingTaxAmount + withHoldingVatAmount)
                });
            }
            return Json(null);
        }

        [HttpGet]
        public async Task<IActionResult> CollectionEdit(int? id, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                return NotFound();
            }
            var existingModel = await _dbContext.CollectionReceipts.FirstOrDefaultAsync(x => x.CollectionReceiptId == id, cancellationToken);

            if (existingModel == null)
            {
                return NotFound();
            }

            existingModel.Customers = await _dbContext.Customers
               .OrderBy(c => c.CustomerId)
               .Select(s => new SelectListItem
               {
                   Value = s.CustomerId.ToString(),
                   Text = s.CustomerName
               })
               .ToListAsync(cancellationToken);

            existingModel.SalesInvoices = await _dbContext.SalesInvoices
                .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                .OrderBy(si => si.SalesInvoiceId)
                .Select(s => new SelectListItem
                {
                    Value = s.SalesInvoiceId.ToString(),
                    Text = s.SalesInvoiceNo
                })
                .ToListAsync(cancellationToken);

            existingModel.ServiceInvoices = await _dbContext.ServiceInvoices
                .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                .OrderBy(si => si.ServiceInvoiceId)
                .Select(s => new SelectListItem
                {
                    Value = s.ServiceInvoiceId.ToString(),
                    Text = s.ServiceInvoiceNo
                })
                .ToListAsync(cancellationToken);

            existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            var findCustomers = await _dbContext.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == existingModel.CustomerId, cancellationToken);

            var offsettings = await _dbContext.Offsettings
                .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                .ToListAsync(cancellationToken);

            ViewBag.CustomerName = findCustomers?.CustomerName;
            ViewBag.Offsettings = offsettings;

            return View(existingModel);
        }

        [HttpPost]
        public async Task<IActionResult> CollectionEdit(CollectionReceipt model, string[] accountTitleText, decimal[] accountAmount, string[] accountTitle, IFormFile? bir2306, IFormFile? bir2307, CancellationToken cancellationToken)
        {
            var existingModel = await _receiptRepo.FindCR(model.CollectionReceiptId, cancellationToken);

            var offsettings = await _dbContext.Offsettings
                .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                .ToListAsync(cancellationToken);

            ViewBag.Offsettings = offsettings;
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Saving default value

                    var computeTotalInModelIfZero = model.CashAmount + model.CheckAmount + model.ManagerCheckAmount + model.EWT + model.WVAT;
                    if (computeTotalInModelIfZero == 0)
                    {
                        TempData["error"] = "Please input atleast one type form of payment";
                        existingModel.Customers = await _dbContext.Customers
                            .OrderBy(c => c.CustomerId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.CustomerId.ToString(),
                                Text = s.CustomerName
                            })
                            .ToListAsync(cancellationToken);

                        existingModel.SalesInvoices = await _dbContext.SalesInvoices
                            .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                            .OrderBy(si => si.SalesInvoiceId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.SalesInvoiceId.ToString(),
                                Text = s.SalesInvoiceNo
                            })
                            .ToListAsync(cancellationToken);

                        existingModel.ServiceInvoices = await _dbContext.ServiceInvoices
                            .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                            .OrderBy(si => si.ServiceInvoiceId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.ServiceInvoiceId.ToString(),
                                Text = s.ServiceInvoiceNo
                            })
                            .ToListAsync(cancellationToken);

                        existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                            .Where(coa => !coa.HasChildren)
                            .OrderBy(coa => coa.AccountId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        return View(existingModel);
                    }

                    existingModel.TransactionDate = model.TransactionDate;
                    existingModel.ReferenceNo = model.ReferenceNo;
                    existingModel.Remarks = model.Remarks;
                    existingModel.CheckDate = model.CheckDate;
                    existingModel.CheckNo = model.CheckNo;
                    existingModel.CheckBank = model.CheckBank;
                    existingModel.CheckBranch = model.CheckBranch;
                    existingModel.CashAmount = model.CashAmount;
                    existingModel.CheckAmount = model.CheckAmount;
                    existingModel.ManagerCheckAmount = model.ManagerCheckAmount;
                    existingModel.EWT = model.EWT;
                    existingModel.WVAT = model.WVAT;
                    existingModel.Total = computeTotalInModelIfZero;

                        if (bir2306 != null && bir2306.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2306");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            var fileName = Path.GetFileName(bir2306.FileName);
                            var fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2306.CopyToAsync(stream, cancellationToken);
                            }

                            existingModel.F2306FilePath = fileSavePath;
                            existingModel.IsCertificateUpload = true;
                        }

                        if (bir2307 != null && bir2307.Length > 0)
                        {
                            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2307");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            var fileName = Path.GetFileName(bir2307.FileName);
                            var fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2307.CopyToAsync(stream, cancellationToken);
                            }

                            existingModel.F2307FilePath = fileSavePath;
                            existingModel.IsCertificateUpload = true;
                        }

                        #endregion --Saving default value

                    #region --Offsetting function

                    var findOffsettings = await _dbContext.Offsettings
                    .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                    .ToListAsync(cancellationToken);

                    var accountTitleSet = new HashSet<string>(accountTitle);

                    // Remove records not in accountTitle
                    foreach (var offsetting in findOffsettings)
                    {
                        if (!accountTitleSet.Contains(offsetting.AccountNo))
                        {
                            _dbContext.Offsettings.Remove(offsetting);
                        }
                    }

                    // Dictionary to keep track of AccountNo and their ids for comparison
                    var accountTitleDict = new Dictionary<string, List<int>>();
                    foreach (var offsetting in findOffsettings)
                    {
                        if (!accountTitleDict.ContainsKey(offsetting.AccountNo))
                        {
                            accountTitleDict[offsetting.AccountNo] = new List<int>();
                        }
                        accountTitleDict[offsetting.AccountNo].Add(offsetting.Id);
                    }

                    // Add or update records
                    for (int i = 0; i < accountTitle.Length; i++)
                    {
                        var accountNo = accountTitle[i];
                        var currentAccountTitle = accountTitleText[i];
                        var currentAccountAmount = accountAmount[i];

                        var splitAccountTitle = currentAccountTitle.Split([' '], 2);

                        if (accountTitleDict.TryGetValue(accountNo, out var ids))
                        {
                            // Update the first matching record and remove it from the list
                            var offsettingId = ids.First();
                            ids.RemoveAt(0);
                            var offsetting = findOffsettings.First(o => o.Id == offsettingId);

                            offsetting.AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0];
                            offsetting.Amount = currentAccountAmount;

                            if (ids.Count == 0)
                            {
                                accountTitleDict.Remove(accountNo);
                            }
                        }
                        else
                        {
                            // Add new record
                            var newOffsetting = new Offsetting
                            {
                                AccountNo = accountNo,
                                AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0],
                                Source = existingModel.CollectionReceiptNo!,
                                Reference = existingModel.SINo ?? existingModel.SVNo,
                                Amount = currentAccountAmount,
                            };
                            await _dbContext.Offsettings.AddAsync(newOffsetting, cancellationToken);
                        }
                    }

                    // Remove remaining records that were duplicates
                    foreach (var ids in accountTitleDict.Values)
                    {
                        foreach (var id in ids)
                        {
                            var offsetting = findOffsettings.First(o => o.Id == id);
                            _dbContext.Offsettings.Remove(offsetting);
                        }
                    }

                    #endregion --Offsetting function

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (existingModel.OriginalSeriesNumber.IsNullOrEmpty() && existingModel.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            var modifiedBy = _userManager.GetUserName(this.User);
                            AuditTrail auditTrailBook = new(modifiedBy!, $"Edited collection receipt# {existingModel.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Collection Receipt edited successfully";
                        return RedirectToAction(nameof(CollectionIndex));
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 existingModel.Customers = await _dbContext.Customers
                     .OrderBy(c => c.CustomerId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.CustomerId.ToString(),
                         Text = s.CustomerName
                     })
                     .ToListAsync(cancellationToken);

                 existingModel.SalesInvoices = await _dbContext.SalesInvoices
                     .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                     .OrderBy(si => si.SalesInvoiceId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.SalesInvoiceId.ToString(),
                         Text = s.SalesInvoiceNo
                     })
                     .ToListAsync(cancellationToken);

                 existingModel.ServiceInvoices = await _dbContext.ServiceInvoices
                     .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                     .OrderBy(si => si.ServiceInvoiceId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.ServiceInvoiceId.ToString(),
                         Text = s.ServiceInvoiceNo
                     })
                     .ToListAsync(cancellationToken);

                 existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                     .Where(coa => !coa.HasChildren)
                     .OrderBy(coa => coa.AccountId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.AccountNumber,
                         Text = s.AccountNumber + " " + s.AccountName
                     })
                     .ToListAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return View(existingModel);
                }
            }
            else
            {
                TempData["error"] = "The information you submitted is not valid!";
                existingModel.Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.CustomerId.ToString(),
                        Text = s.CustomerName
                    })
                    .ToListAsync(cancellationToken);

                existingModel.SalesInvoices = await _dbContext.SalesInvoices
                    .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                    .OrderBy(si => si.SalesInvoiceId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.SalesInvoiceId.ToString(),
                        Text = s.SalesInvoiceNo
                    })
                    .ToListAsync(cancellationToken);

                existingModel.ServiceInvoices = await _dbContext.ServiceInvoices
                    .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId && si.IsPosted)
                    .OrderBy(si => si.ServiceInvoiceId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.ServiceInvoiceId.ToString(),
                        Text = s.ServiceInvoiceNo
                    })
                    .ToListAsync(cancellationToken);

                existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken);
                return View(existingModel);
            }
        }

        [HttpGet]
        public async Task<IActionResult> MultipleCollectionEdit(int? id, CancellationToken cancellationToken)
        {
            if (id == null)
            {
                return NotFound();
            }
            var existingModel = await _dbContext.CollectionReceipts.FirstOrDefaultAsync(x => x.CollectionReceiptId == id, cancellationToken);

            if (existingModel == null)
            {
                return NotFound();
            }

            existingModel.Customers = await _dbContext.Customers
               .OrderBy(c => c.CustomerId)
               .Select(s => new SelectListItem
               {
                   Value = s.CustomerId.ToString(),
                   Text = s.CustomerName
               })
               .ToListAsync(cancellationToken);

            if (existingModel.MultipleSIId != null)
            {
                existingModel.SalesInvoices = await _dbContext.SalesInvoices
                    .Where(si => !si.IsPaid && existingModel.MultipleSIId.Contains(si.SalesInvoiceId))
                    .OrderBy(si => si.SalesInvoiceId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.SalesInvoiceId.ToString(),
                        Text = s.SalesInvoiceNo
                    })
                    .ToListAsync(cancellationToken);
            }

            existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            var findCustomers = await _dbContext.Customers
                .FirstOrDefaultAsync(c => c.CustomerId == existingModel.CustomerId, cancellationToken);

            var offsettings = await _dbContext.Offsettings
                .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                .ToListAsync(cancellationToken);

            ViewBag.CustomerName = findCustomers?.CustomerName;
            ViewBag.Offsettings = offsettings;

            return View(existingModel);
        }

        [HttpPost]
        public async Task<IActionResult> MultipleCollectionEdit(CollectionReceipt model, string[] accountTitleText, decimal[] accountAmount, string[] accountTitle, IFormFile? bir2306, IFormFile? bir2307, CancellationToken cancellationToken)
        {
            var existingModel = await _receiptRepo.FindCR(model.CollectionReceiptId, cancellationToken);

            var offsettings = await _dbContext.Offsettings
                .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                .ToListAsync(cancellationToken);

            ViewBag.Offsettings = offsettings;
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region --Saving default value

                    var computeTotalInModelIfZero = model.CashAmount + model.CheckAmount + model.ManagerCheckAmount + model.EWT + model.WVAT;
                    if (computeTotalInModelIfZero == 0)
                    {
                        TempData["error"] = "Please input atleast one type form of payment";
                        existingModel.Customers = await _dbContext.Customers
                            .OrderBy(c => c.CustomerId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.CustomerId.ToString(),
                                Text = s.CustomerName
                            })
                            .ToListAsync(cancellationToken);

                        existingModel.SalesInvoices = await _dbContext.SalesInvoices
                            .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId)
                            .OrderBy(si => si.SalesInvoiceId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.SalesInvoiceId.ToString(),
                                Text = s.SalesInvoiceNo
                            })
                            .ToListAsync(cancellationToken);

                        existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                            .Where(coa => !coa.HasChildren)
                            .OrderBy(coa => coa.AccountId)
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        return View(existingModel);
                    }

                    existingModel.MultipleSIId = new int[model.MultipleSIId!.Length];
                    existingModel.MultipleSI = new string[model.MultipleSIId.Length];
                    existingModel.SIMultipleAmount = new decimal[model.MultipleSIId.Length];
                    existingModel.MultipleTransactionDate = new DateOnly[model.MultipleSIId.Length];
                    for (int i = 0; i < model.MultipleSIId.Length; i++)
                    {
                        var siId = model.MultipleSIId[i];
                        var salesInvoice = await _dbContext.SalesInvoices
                            .FirstOrDefaultAsync(si => si.SalesInvoiceId == siId, cancellationToken);

                        if (salesInvoice != null)
                        {
                            existingModel.MultipleSIId[i] = model.MultipleSIId[i];
                            existingModel.MultipleSI[i] = salesInvoice.SalesInvoiceNo!;
                            existingModel.MultipleTransactionDate[i] = salesInvoice.TransactionDate;
                            existingModel.SIMultipleAmount[i] = model.SIMultipleAmount![i];
                        }
                    }

                    existingModel.TransactionDate = model.TransactionDate;
                    existingModel.ReferenceNo = model.ReferenceNo;
                    existingModel.Remarks = model.Remarks;
                    existingModel.CheckDate = model.CheckDate;
                    existingModel.CheckNo = model.CheckNo;
                    existingModel.CheckBank = model.CheckBank;
                    existingModel.CheckBranch = model.CheckBranch;
                    existingModel.CashAmount = model.CashAmount;
                    existingModel.CheckAmount = model.CheckAmount;
                    existingModel.ManagerCheckAmount = model.ManagerCheckAmount;
                    existingModel.EWT = model.EWT;
                    existingModel.WVAT = model.WVAT;
                    existingModel.Total = computeTotalInModelIfZero;

                        if (bir2306 != null && bir2306.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2306");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2306.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2306.CopyToAsync(stream, cancellationToken);
                            }

                            existingModel.F2306FilePath = fileSavePath;
                            existingModel.IsCertificateUpload = true;
                        }

                        if (bir2307 != null && bir2307.Length > 0)
                        {
                            string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "BIR 2307");

                            if (!Directory.Exists(uploadsFolder))
                            {
                                Directory.CreateDirectory(uploadsFolder);
                            }

                            string fileName = Path.GetFileName(bir2307.FileName);
                            string fileSavePath = Path.Combine(uploadsFolder, fileName);

                            await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                            {
                                await bir2307.CopyToAsync(stream, cancellationToken);
                            }

                            existingModel.F2307FilePath = fileSavePath;
                            existingModel.IsCertificateUpload = true;
                        }

                    #endregion --Saving default value

                    #region --Offsetting function

                    var findOffsettings = await _dbContext.Offsettings
                    .Where(offset => offset.Source == existingModel.CollectionReceiptNo)
                    .ToListAsync(cancellationToken);

                    var accountTitleSet = new HashSet<string>(accountTitle);

                    // Remove records not in accountTitle
                    foreach (var offsetting in findOffsettings)
                    {
                        if (!accountTitleSet.Contains(offsetting.AccountNo))
                        {
                            _dbContext.Offsettings.Remove(offsetting);
                        }
                    }

                    // Dictionary to keep track of AccountNo and their ids for comparison
                    var accountTitleDict = new Dictionary<string, List<int>>();
                    foreach (var offsetting in findOffsettings)
                    {
                        if (!accountTitleDict.ContainsKey(offsetting.AccountNo))
                        {
                            accountTitleDict[offsetting.AccountNo] = new List<int>();
                        }
                        accountTitleDict[offsetting.AccountNo].Add(offsetting.Id);
                    }

                    // Add or update records
                    for (int i = 0; i < accountTitle.Length; i++)
                    {
                        var accountNo = accountTitle[i];
                        var currentAccountTitle = accountTitleText[i];
                        var currentAccountAmount = accountAmount[i];

                        var splitAccountTitle = currentAccountTitle.Split([' '], 2);

                        if (accountTitleDict.TryGetValue(accountNo, out var ids))
                        {
                            // Update the first matching record and remove it from the list
                            var offsettingId = ids.First();
                            ids.RemoveAt(0);
                            var offsetting = findOffsettings.First(o => o.Id == offsettingId);

                            offsetting.AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0];
                            offsetting.Amount = currentAccountAmount;

                            if (ids.Count == 0)
                            {
                                accountTitleDict.Remove(accountNo);
                            }
                        }
                        else
                        {
                            // Add new record
                            var newOffsetting = new Offsetting
                            {
                                AccountNo = accountNo,
                                AccountTitle = splitAccountTitle.Length > 1 ? splitAccountTitle[1] : splitAccountTitle[0],
                                Source = existingModel.CollectionReceiptNo!,
                                Reference = existingModel.SINo ?? existingModel.SVNo,
                                Amount = currentAccountAmount,
                            };
                            await _dbContext.Offsettings.AddAsync(newOffsetting, cancellationToken);
                        }
                    }

                    // Remove remaining records that were duplicates
                    foreach (var ids in accountTitleDict.Values)
                    {
                        foreach (var id in ids)
                        {
                            var offsetting = findOffsettings.First(o => o.Id == id);
                            _dbContext.Offsettings.Remove(offsetting);
                        }
                    }

                    #endregion --Offsetting function

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (existingModel.OriginalSeriesNumber.IsNullOrEmpty() && existingModel.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            var modifiedBy = _userManager.GetUserName(this.User);
                            AuditTrail auditTrailBook = new(modifiedBy!, $"Edited collection receipt# {existingModel.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Collection Receipt edited successfully";
                        return RedirectToAction(nameof(CollectionIndex));
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 existingModel.Customers = await _dbContext.Customers
                     .OrderBy(c => c.CustomerId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.CustomerId.ToString(),
                         Text = s.CustomerName
                     })
                     .ToListAsync(cancellationToken);

                 existingModel.SalesInvoices = await _dbContext.SalesInvoices
                     .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId)
                     .OrderBy(si => si.SalesInvoiceId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.SalesInvoiceId.ToString(),
                         Text = s.SalesInvoiceNo
                     })
                     .ToListAsync(cancellationToken);

                 existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                     .Where(coa => !coa.HasChildren)
                     .OrderBy(coa => coa.AccountId)
                     .Select(s => new SelectListItem
                     {
                         Value = s.AccountNumber,
                         Text = s.AccountNumber + " " + s.AccountName
                     })
                     .ToListAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return View(existingModel);
                }
            }
            else
            {
                TempData["error"] = "The information you submitted is not valid!";
                existingModel.Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.CustomerId.ToString(),
                        Text = s.CustomerName
                    })
                    .ToListAsync(cancellationToken);

                existingModel.SalesInvoices = await _dbContext.SalesInvoices
                    .Where(si => !si.IsPaid && si.CustomerId == existingModel.CustomerId)
                    .OrderBy(si => si.SalesInvoiceId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.SalesInvoiceId.ToString(),
                        Text = s.SalesInvoiceNo
                    })
                    .ToListAsync(cancellationToken);

                existingModel.ChartOfAccounts = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken);
                return View(existingModel);
            }
        }

        public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
        {
            var model = await _receiptRepo.FindCR(id, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var collectionPrint = model.MultipleSIId != null ? nameof(MultipleCollectionPrint) : nameof(CollectionPrint);
            try
            {
                if (!model.IsPosted)
                {
                    model.IsPosted = true;
                    model.PostedBy = _userManager.GetUserName(this.User);
                    model.PostedDate = DateTime.Now;

                    List<Offsetting>? offset;
                    decimal offsetAmount = 0;

                    if (model.SalesInvoiceId != null)
                    {
                        offset = await _receiptRepo.GetOffsettingAsync(model.CollectionReceiptNo!, model.SINo!, cancellationToken);
                        if (offset.Any())
                        {
                            offsetAmount = offset.Sum(o => o.Amount);
                        }
                    }
                    else
                    {
                        offset = await _receiptRepo.GetOffsettingAsync(model.CollectionReceiptNo!, model.SVNo!, cancellationToken);
                        if (offset.Any())
                        {
                            offsetAmount = offset.Sum(o => o.Amount);
                        }
                    }

                    await _receiptRepo.PostAsync(model, offset, cancellationToken);

                    if (model.SalesInvoiceId != null)
                    {
                        await _receiptRepo.UpdateInvoice(model.SalesInvoice!.SalesInvoiceId, model.Total, offsetAmount, cancellationToken);
                    }
                    else if (model.MultipleSIId != null)
                    {
                        await _receiptRepo.UpdateMultipleInvoice(model.MultipleSI!, model.SIMultipleAmount!, offsetAmount, cancellationToken);
                    }
                    else
                    {
                        await _receiptRepo.UpdateSv(model.ServiceInvoice!.ServiceInvoiceId, model.Total, offsetAmount, cancellationToken);
                    }

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.PostedBy!, $"Posted collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Collection Receipt has been Posted.";
                }
                return RedirectToAction(collectionPrint, new { id });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return RedirectToAction(collectionPrint, new { id });
            }
        }

        public async Task<IActionResult> Void(int id, CancellationToken cancellationToken)
        {
            var model = await _receiptRepo.FindCR(id, cancellationToken);

            if (!model.IsVoided)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (model.IsPosted)
                    {
                        model.IsPosted = false;
                    }

                    model.IsVoided = true;
                    model.VoidedBy = User.Identity!.Name;
                    model.VoidedDate = DateTime.Now;
                    var series = model.SINo ?? model.SVNo;

                    var findOffsetting = await _dbContext.Offsettings.Where(offset => offset.Source == model.CollectionReceiptNo && offset.Reference == series).ToListAsync(cancellationToken);

                    await _generalRepo.RemoveRecords<CashReceiptBook>(crb => crb.RefNo == model.CollectionReceiptNo, cancellationToken);
                    await _generalRepo.RemoveRecords<GeneralLedgerBook>(gl => gl.Reference == model.CollectionReceiptNo, cancellationToken);

                    if (findOffsetting.Any())
                    {
                        await _generalRepo.RemoveRecords<Offsetting>(offset => offset.Source == model.CollectionReceiptNo && offset.Reference == series, cancellationToken);
                    }
                    if (model.SINo != null)
                    {
                        await _receiptRepo.RemoveSIPayment(model.SalesInvoice!.SalesInvoiceId, model.Total, findOffsetting.Sum(offset => offset.Amount), cancellationToken);
                    }
                    else if (model.SVNo != null)
                    {
                        await _receiptRepo.RemoveSVPayment(model.ServiceInvoiceId, model.Total, findOffsetting.Sum(offset => offset.Amount), cancellationToken);
                    }
                    else if (model.MultipleSI != null)
                    {
                        await _receiptRepo.RemoveMultipleSIPayment(model.MultipleSIId!, model.SIMultipleAmount!, findOffsetting.Sum(offset => offset.Amount), cancellationToken);
                    }
                    else
                    {
                        TempData["error"] = "No series number found";
                        return RedirectToAction(nameof(CollectionIndex));
                    }

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.VoidedBy!, $"Voided collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Collection Receipt has been Voided.";

                    return RedirectToAction(nameof(CollectionIndex));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                }
            }

            return NotFound();
        }

        public async Task<IActionResult> Cancel(int id, string cancellationRemarks, CancellationToken cancellationToken)
        {
            var model = await _dbContext.CollectionReceipts.FirstOrDefaultAsync(x => x.CollectionReceiptId == id, cancellationToken);
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
                            AuditTrail auditTrailBook = new(model.CanceledBy!, $"Cancelled collection receipt# {model.CollectionReceiptNo}", "Collection Receipt", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Collection Receipt has been Cancelled.";
                    }
                    return RedirectToAction(nameof(CollectionIndex));
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(CollectionIndex));
            }

            return NotFound();
        }

        //Upload as .xlsx file.(Import)
        #region -- import xlsx record --

        [HttpPost]
        public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return RedirectToAction(nameof(CollectionIndex));
            }

            using (var stream = new MemoryStream())
            {
                await file.CopyToAsync(stream, cancellationToken);
                stream.Position = 0;
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    using var package = new ExcelPackage(stream);
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "CollectionReceipt");

                    var worksheet2 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "Offsetting");

                    var worksheet3 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "SalesInvoice");

                    var worksheet4 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "ServiceInvoice");

                    if (worksheet == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets.";
                        return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
                    }
                    if (worksheet.ToString() != "CollectionReceipt")
                    {
                        TempData["error"] = "The Excel file is not related to collection receipt.";
                        return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
                    }

                    #region -- Sales Invoice Import --

                    var siRowCount = worksheet3?.Dimension?.Rows ?? 0;
                    var siDictionary = new Dictionary<string, bool>();
                    var invoiceList = await _dbContext
                        .SalesInvoices
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= siRowCount; row++) // Assuming the first row is the header
                    {
                        if (worksheet3 == null || siRowCount == 0)
                        {
                            continue;
                        }
                        var invoice = new SalesInvoice
                        {
                            SalesInvoiceNo = worksheet3.Cells[row, 21].Text,
                            OtherRefNo = worksheet3.Cells[row, 1].Text,
                            Quantity = decimal.TryParse(worksheet3.Cells[row, 2].Text, out decimal quantity)
                                ? quantity
                                : 0,
                            UnitPrice = decimal.TryParse(worksheet3.Cells[row, 3].Text, out decimal unitPrice)
                                ? unitPrice
                                : 0,
                            Amount =
                                decimal.TryParse(worksheet3.Cells[row, 4].Text, out decimal amount) ? amount : 0,
                            Remarks = worksheet3.Cells[row, 5].Text,
                            Status = worksheet3.Cells[row, 6].Text,
                            TransactionDate =
                                DateOnly.TryParse(worksheet3.Cells[row, 7].Text, out DateOnly transactionDate)
                                    ? transactionDate
                                    : default,
                            Discount = decimal.TryParse(worksheet3.Cells[row, 8].Text, out decimal discount)
                                ? discount
                                : 0,
                            // AmountPaid = decimal.TryParse(worksheet.Cells[row, 9].Text, out decimal amountPaid)
                            //     ? amountPaid
                            //     : 0,
                            // Balance = decimal.TryParse(worksheet.Cells[row, 10].Text, out decimal balance)
                            //     ? balance
                            //     : 0,
                            // IsPaid = bool.TryParse(worksheet.Cells[row, 11].Text, out bool isPaid) ? isPaid : false,
                            // IsTaxAndVatPaid = bool.TryParse(worksheet.Cells[row, 12].Text, out bool isTaxAndVatPaid)
                            //     ? isTaxAndVatPaid
                            //     : false,
                            DueDate = DateOnly.TryParse(worksheet3.Cells[row, 13].Text, out DateOnly dueDate)
                                ? dueDate
                                : default,
                            CreatedBy = worksheet3.Cells[row, 14].Text,
                            CreatedDate = DateTime.TryParse(worksheet3.Cells[row, 15].Text, out DateTime createdDate)
                                ? createdDate
                                : default,
                            PostedBy = worksheet3.Cells[row, 23].Text,
                            PostedDate = DateTime.TryParse(worksheet3.Cells[row, 24].Text, out DateTime postedDate)
                                ? postedDate
                                : default,
                            CancellationRemarks = worksheet3.Cells[row, 16].Text != ""
                                ? worksheet3.Cells[row, 16].Text
                                : null,
                            OriginalCustomerId = int.TryParse(worksheet3.Cells[row, 18].Text, out int customerId)
                                ? customerId
                                : 0,
                            OriginalProductId = int.TryParse(worksheet3.Cells[row, 20].Text, out int productId)
                                ? productId
                                : 0,
                            OriginalSeriesNumber = worksheet3.Cells[row, 21].Text,
                            OriginalDocumentId =
                                int.TryParse(worksheet3.Cells[row, 22].Text, out int originalDocumentId)
                                    ? originalDocumentId
                                    : 0,
                        };

                        if (!siDictionary.TryAdd(invoice.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        if (invoiceList.Any(si => si.OriginalDocumentId == invoice.OriginalDocumentId))
                        {
                            var siChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingSalesInvoice = await _dbContext.SalesInvoices.FirstOrDefaultAsync(si => si.OriginalDocumentId == invoice.OriginalDocumentId, cancellationToken);

                            if (existingSalesInvoice!.SalesInvoiceNo!.TrimStart().TrimEnd() != worksheet3.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                siChanges["SiNo"] = (existingSalesInvoice.SalesInvoiceNo.TrimStart().TrimEnd(), worksheet3.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.OriginalCustomerId.ToString()!.TrimStart().TrimEnd() != worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalCustomerId"] = (existingSalesInvoice.OriginalCustomerId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 18].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.OriginalProductId.ToString()!.TrimStart().TrimEnd() != worksheet3.Cells[row, 20].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalProductId"] = (existingSalesInvoice.OriginalProductId.ToString()!.TrimStart().TrimEnd(), worksheet3.Cells[row, 20].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.OtherRefNo.TrimStart().TrimEnd() != worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OtherRefNo"] = (existingSalesInvoice.OtherRefNo.TrimStart().TrimEnd(), worksheet3.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.Quantity.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 2].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Quantity"] = (existingSalesInvoice.Quantity.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 2].Text.TrimStart().TrimEnd()).ToString("F2"));
                            }

                            if (existingSalesInvoice.UnitPrice.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["UnitPrice"] = (existingSalesInvoice.UnitPrice.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Amount"] = (existingSalesInvoice.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.Remarks.TrimStart().TrimEnd() != worksheet3.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                siChanges["Remarks"] = (existingSalesInvoice.Remarks.TrimStart().TrimEnd(), worksheet3.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.Status.TrimStart().TrimEnd() != worksheet3.Cells[row, 6].Text.TrimStart().TrimEnd())
                            {
                                siChanges["Status"] = (existingSalesInvoice.Status.TrimStart().TrimEnd(), worksheet3.Cells[row, 6].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet3.Cells[row, 7].Text.TrimStart().TrimEnd())
                            {
                                siChanges["TransactionDate"] = (existingSalesInvoice.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet3.Cells[row, 7].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.Discount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet3.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Discount"] = (existingSalesInvoice.Discount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet3.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                siChanges["DueDate"] = (existingSalesInvoice.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet3.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.CreatedBy!.TrimStart().TrimEnd() != worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CreatedBy"] = (existingSalesInvoice.CreatedBy.TrimStart().TrimEnd(), worksheet3.Cells[row, 14].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CreatedDate"] = (existingSalesInvoice.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet3.Cells[row, 15].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingSalesInvoice.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingSalesInvoice.CancellationRemarks.TrimStart().TrimEnd()) != worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CancellationRemarks"] = (existingSalesInvoice.CancellationRemarks?.TrimStart().TrimEnd(), worksheet3.Cells[row, 16].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingSalesInvoice.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet3.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalSeriesNumber"] = (existingSalesInvoice.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet3.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingSalesInvoice.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet3.Cells[row, 22].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalDocumentId"] = (existingSalesInvoice.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet3.Cells[row, 22].Text.TrimStart().TrimEnd());
                            }

                            if (siChanges.Any())
                            {
                                await _salesInvoiceRepo.LogChangesAsync(existingSalesInvoice.OriginalDocumentId, siChanges, _userManager.GetUserName(this.User), existingSalesInvoice.SalesInvoiceNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!invoice.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(invoice.CreatedBy, $"Create new invoice# {invoice.SalesInvoiceNo}", "Sales Invoice", ipAddress!, invoice.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!invoice.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(invoice.PostedBy, $"Posted invoice# {invoice.SalesInvoiceNo}", "Sales Invoice", ipAddress!, invoice.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        invoice.CustomerId = await _dbContext.Customers
                            .Where(c => c.OriginalCustomerId == invoice.OriginalCustomerId)
                            .Select(c => (int?)c.CustomerId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the customer master file first.");

                        invoice.ProductId = await _dbContext.Products
                            .Where(c => c.OriginalProductId == invoice.OriginalProductId)
                            .Select(c => (int?)c.ProductId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the product master file first.");

                        await _dbContext.SalesInvoices.AddAsync(invoice, cancellationToken);
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Sales Invoice Import --

                    #region -- Service Invoice Import --

                    var svRowCount = worksheet4?.Dimension?.Rows ?? 0;
                    var svDictionary = new Dictionary<string, bool>();
                    var serviceInvoiceList = await _dbContext
                        .ServiceInvoices
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= svRowCount; row++)  // Assuming the first row is the header
                    {
                        if (worksheet4 == null || svRowCount == 0)
                        {
                            continue;
                        }
                        var serviceInvoice = new ServiceInvoice
                        {
                            ServiceInvoiceNo = worksheet4.Cells[row, 17].Text,
                            DueDate = DateOnly.TryParse(worksheet4.Cells[row, 1].Text, out DateOnly dueDate) ? dueDate : default,
                            Period = DateOnly.TryParse(worksheet4.Cells[row, 2].Text, out DateOnly period) ? period : default,
                            Amount = decimal.TryParse(worksheet4.Cells[row, 3].Text, out decimal amount) ? amount : 0,
                            Total = decimal.TryParse(worksheet4.Cells[row, 4].Text, out decimal total) ? total : 0,
                            Discount = decimal.TryParse(worksheet4.Cells[row, 5].Text, out decimal discount) ? discount : 0,
                            CurrentAndPreviousAmount = decimal.TryParse(worksheet4.Cells[row, 6].Text, out decimal currentAndPreviousAmount) ? currentAndPreviousAmount : 0,
                            UnearnedAmount = decimal.TryParse(worksheet4.Cells[row, 7].Text, out decimal unearnedAmount) ? unearnedAmount : 0,
                            Status = worksheet4.Cells[row, 8].Text,
                            // AmountPaid = decimal.TryParse(worksheet.Cells[row, 9].Text, out decimal amountPaid) ? amountPaid : 0,
                            // Balance = decimal.TryParse(worksheet.Cells[row, 10].Text, out decimal balance) ? balance : 0,
                            Instructions = worksheet4.Cells[row, 11].Text,
                            // IsPaid = bool.TryParse(worksheet.Cells[row, 12].Text, out bool isPaid) ? isPaid : false,
                            CreatedBy = worksheet4.Cells[row, 13].Text,
                            CreatedDate = DateTime.TryParse(worksheet4.Cells[row, 14].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet4.Cells[row, 20].Text,
                            PostedDate = DateTime.TryParse(worksheet4.Cells[row, 21].Text, out DateTime postedDate) ? postedDate : default,
                            CancellationRemarks = worksheet4.Cells[row, 15].Text,
                            OriginalCustomerId = int.TryParse(worksheet4.Cells[row, 16].Text, out int originalCustomerId) ? originalCustomerId : 0,
                            OriginalSeriesNumber = worksheet4.Cells[row, 17].Text,
                            OriginalServicesId = int.TryParse(worksheet4.Cells[row, 18].Text, out int originalServicesId) ? originalServicesId : 0,
                            OriginalDocumentId = int.TryParse(worksheet4.Cells[row, 19].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        if (!svDictionary.TryAdd(serviceInvoice.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        if (serviceInvoiceList.Any(sv => sv.OriginalDocumentId == serviceInvoice.OriginalDocumentId))
                        {
                            var svChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingServiceInvoice = await _dbContext.ServiceInvoices.FirstOrDefaultAsync(si => si.OriginalDocumentId == serviceInvoice.OriginalDocumentId, cancellationToken);

                            if (existingServiceInvoice!.ServiceInvoiceNo!.TrimStart().TrimEnd() != worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd())
                            {
                                svChanges["SvNo"] = (existingServiceInvoice.ServiceInvoiceNo.TrimStart().TrimEnd(), worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                svChanges["DueDate"] = (existingServiceInvoice.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Period.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                svChanges["Period"] = (existingServiceInvoice.Period.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet4.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                svChanges["Amount"] = (existingServiceInvoice.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Total.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                svChanges["Total"] = (existingServiceInvoice.Total.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Discount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                svChanges["Discount"] = (existingServiceInvoice.Discount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.CurrentAndPreviousAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                svChanges["CurrentAndPreviousAmount"] = (existingServiceInvoice.CurrentAndPreviousAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.UnearnedAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                svChanges["UnearnedAmount"] = (existingServiceInvoice.UnearnedAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet4.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Status.TrimStart().TrimEnd() != worksheet4.Cells[row, 8].Text.TrimStart().TrimEnd())
                            {
                                svChanges["Status"] = (existingServiceInvoice.Status.TrimStart().TrimEnd(), worksheet4.Cells[row, 8].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.Instructions!.TrimStart().TrimEnd() != worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                svChanges["Instructions"] = (existingServiceInvoice.Instructions.TrimStart().TrimEnd(), worksheet4.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.CreatedBy!.TrimStart().TrimEnd() != worksheet4.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                svChanges["CreatedBy"] = (existingServiceInvoice.CreatedBy.TrimStart().TrimEnd(), worksheet4.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet4.Cells[row, 14].Text.TrimStart().TrimEnd())
                            {
                                svChanges["CreatedDate"] = (existingServiceInvoice.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet4.Cells[row, 14].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingServiceInvoice.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingServiceInvoice.CancellationRemarks.TrimStart().TrimEnd()) != worksheet4.Cells[row, 15].Text.TrimStart().TrimEnd())
                            {
                                svChanges["CancellationRemarks"] = (existingServiceInvoice.CancellationRemarks?.TrimStart().TrimEnd(), worksheet4.Cells[row, 15].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingServiceInvoice.OriginalCustomerId.ToString()!.TrimStart().TrimEnd() != worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                svChanges["OriginalCustomerId"] = (existingServiceInvoice.OriginalCustomerId.ToString()!.TrimStart().TrimEnd(), worksheet4.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd())
                            {
                                svChanges["OriginalSeriesNumber"] = (existingServiceInvoice.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet4.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.OriginalServicesId.ToString()!.TrimStart().TrimEnd() != worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                svChanges["OriginalServicesId"] = (existingServiceInvoice.OriginalServicesId.ToString()!.TrimStart().TrimEnd(), worksheet4.Cells[row, 18].Text.TrimStart().TrimEnd());
                            }

                            if (existingServiceInvoice.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd())
                            {
                                svChanges["OriginalDocumentId"] = (existingServiceInvoice.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet4.Cells[row, 19].Text.TrimStart().TrimEnd());
                            }

                            if (svChanges.Any())
                            {
                                await _serviceInvoiceRepo.LogChangesAsync(existingServiceInvoice.OriginalDocumentId, svChanges, _userManager.GetUserName(this.User), existingServiceInvoice.ServiceInvoiceNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!serviceInvoice.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(serviceInvoice.CreatedBy, $"Create new service invoice# {serviceInvoice.ServiceInvoiceNo}", "Service Invoice", ipAddress!, serviceInvoice.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!serviceInvoice.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(serviceInvoice.PostedBy, $"Posted service invoice# {serviceInvoice.ServiceInvoiceNo}", "Service Invoice", ipAddress!, serviceInvoice.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        serviceInvoice.CustomerId = await _dbContext.Customers
                            .Where(sv => sv.OriginalCustomerId == serviceInvoice.OriginalCustomerId)
                            .Select(sv => (int?)sv.CustomerId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the customer master file first.");

                        serviceInvoice.ServicesId = await _dbContext.Services
                            .Where(sv => sv.OriginalServiceId == serviceInvoice.OriginalServicesId)
                            .Select(sv => (int?)sv.ServiceId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the service master file first.");

                        await _dbContext.ServiceInvoices.AddAsync(serviceInvoice, cancellationToken);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Service Invoice Import --

                    #region -- Collection Receipt Import --

                    var rowCount = worksheet.Dimension.Rows;
                    var crDictionary = new Dictionary<string, bool>();
                    var collectionReceiptList = await _dbContext
                        .CollectionReceipts
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        var collectionReceipt = new CollectionReceipt
                        {
                            CollectionReceiptNo = worksheet.Cells[row, 30].Text,
                            TransactionDate = DateOnly.TryParse(worksheet.Cells[row, 1].Text, out DateOnly transactionDate) ? transactionDate : default,
                            ReferenceNo = worksheet.Cells[row, 2].Text,
                            Remarks = worksheet.Cells[row, 3].Text,
                            CashAmount = decimal.TryParse(worksheet.Cells[row, 4].Text, out decimal cashAmount) ? cashAmount : 0,
                            CheckDate = DateOnly.TryParse(worksheet.Cells[row, 5].Text, out DateOnly checkDate) ? checkDate : default,
                            CheckNo = worksheet.Cells[row, 6].Text,
                            CheckBank = worksheet.Cells[row, 7].Text,
                            CheckBranch = worksheet.Cells[row, 8].Text,
                            CheckAmount = decimal.TryParse(worksheet.Cells[row, 9].Text, out decimal checkAmount) ? checkAmount : 0,
                            ManagerCheckDate = DateOnly.TryParse(worksheet.Cells[row, 10].Text, out DateOnly managerCheckDate) ? managerCheckDate : null,
                            ManagerCheckNo = worksheet.Cells[row, 11].Text,
                            ManagerCheckBank = worksheet.Cells[row, 12].Text,
                            ManagerCheckBranch = worksheet.Cells[row, 13].Text,
                            ManagerCheckAmount = decimal.TryParse(worksheet.Cells[row, 14].Text, out decimal managerCheckAmount) ? managerCheckAmount : 0,
                            EWT = decimal.TryParse(worksheet.Cells[row, 15].Text, out decimal ewt) ? ewt : 0,
                            WVAT = decimal.TryParse(worksheet.Cells[row, 16].Text, out decimal wvat) ? wvat : 0,
                            Total = decimal.TryParse(worksheet.Cells[row, 17].Text, out decimal total) ? total : 0,
                            IsCertificateUpload = bool.TryParse(worksheet.Cells[row, 18].Text, out bool isCertificateUpload) && isCertificateUpload,
                            F2306FilePath = worksheet.Cells[row, 19].Text,
                            F2307FilePath = worksheet.Cells[row, 20].Text,
                            CreatedBy = worksheet.Cells[row, 21].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 22].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet.Cells[row, 33].Text,
                            PostedDate = DateTime.TryParse(worksheet.Cells[row, 34].Text, out DateTime postedDate) ? postedDate : default,
                            CancellationRemarks = worksheet.Cells[row, 23].Text,
                            MultipleSI = worksheet.Cells[row, 24].Text.Split(',').Select(si => si.Trim()).ToArray(),
                            MultipleSIId = worksheet.Cells[row, 25].Text.Split(',').Select(multipleId => int.TryParse(multipleId.Trim(), out int multipleSiId) ? multipleSiId : 0).ToArray(),
                            SIMultipleAmount = worksheet.Cells[row, 26].Text.Split(' ').Select(multipleAmount => decimal.TryParse(multipleAmount.Trim(), out decimal siMultipleAmount) ? siMultipleAmount : 0).ToArray(),
                            MultipleTransactionDate = worksheet.Cells[row, 27].Text.Split(',').Select(date => DateOnly.TryParse(date.Trim(), out DateOnly parsedDate) ? parsedDate : default).ToArray(),
                            OriginalCustomerId = int.TryParse(worksheet.Cells[row, 28].Text, out int originalCustomerId) ? originalCustomerId : 0,
                            OriginalSalesInvoiceId = int.TryParse(worksheet.Cells[row, 29].Text, out int originalSalesInvoiceId) ? originalSalesInvoiceId : 0,
                            OriginalSeriesNumber = worksheet.Cells[row, 30].Text,
                            OriginalServiceInvoiceId = int.TryParse(worksheet.Cells[row, 31].Text, out int originalServiceInvoiceId) ? originalServiceInvoiceId : 0,
                            OriginalDocumentId = int.TryParse(worksheet.Cells[row, 32].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        if (!crDictionary.TryAdd(collectionReceipt.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        if (collectionReceiptList.Any(cr => cr.OriginalDocumentId == collectionReceipt.OriginalDocumentId))
                        {
                            var crChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingCollectionReceipt = await _dbContext.CollectionReceipts.FirstOrDefaultAsync(si => si.OriginalDocumentId == collectionReceipt.OriginalDocumentId, cancellationToken);

                            if (existingCollectionReceipt!.CollectionReceiptNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 30].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CrNo"] = (existingCollectionReceipt.CollectionReceiptNo.TrimStart().TrimEnd(), worksheet.Cells[row, 30].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                crChanges["TransactionDate"] = (existingCollectionReceipt.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ReferenceNo.TrimStart().TrimEnd() != worksheet.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                crChanges["ReferenceNo"] = (existingCollectionReceipt.ReferenceNo.TrimStart().TrimEnd(), worksheet.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.Remarks!.TrimStart().TrimEnd() != worksheet.Cells[row, 3].Text.TrimStart().TrimEnd())
                            {
                                crChanges["Remarks"] = (existingCollectionReceipt.Remarks.TrimStart().TrimEnd(), worksheet.Cells[row, 3].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CashAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["CashAmount"] = (existingCollectionReceipt.CashAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CheckDate != DateOnly.Parse(worksheet.Cells[row, 5].Text))
                            {
                                crChanges["CheckDate"] = (existingCollectionReceipt.CheckDate!.ToString(), worksheet.Cells[row, 5].Text);
                            }

                            if (existingCollectionReceipt.CheckNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 6].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CheckNo"] = (existingCollectionReceipt.CheckNo.TrimStart().TrimEnd(), worksheet.Cells[row, 6].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CheckBank!.TrimStart().TrimEnd() != worksheet.Cells[row, 7].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CheckBank"] = (existingCollectionReceipt.CheckBank.TrimStart().TrimEnd(), worksheet.Cells[row, 7].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CheckBranch!.TrimStart().TrimEnd() != worksheet.Cells[row, 8].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CheckBranch"] = (existingCollectionReceipt.CheckBranch.TrimStart().TrimEnd(), worksheet.Cells[row, 8].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CheckAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["CheckAmount"] = (existingCollectionReceipt.CheckAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ManagerCheckDate.ToString()!.TrimStart().TrimEnd() != worksheet.Cells[row, 10].Text.TrimStart().TrimEnd())
                            {
                                crChanges["ManagerCheckDate"] = (existingCollectionReceipt.ManagerCheckDate.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 10].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ManagerCheckNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                crChanges["ManagerCheckNo"] = (existingCollectionReceipt.ManagerCheckNo.TrimStart().TrimEnd(), worksheet.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ManagerCheckBank!.TrimStart().TrimEnd() != worksheet.Cells[row, 12].Text.TrimStart().TrimEnd())
                            {
                                crChanges["ManagerCheckBank"] = (existingCollectionReceipt.ManagerCheckBank.TrimStart().TrimEnd(), worksheet.Cells[row, 12].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ManagerCheckBranch!.TrimStart().TrimEnd() != worksheet.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                crChanges["ManagerCheckBranch"] = (existingCollectionReceipt.ManagerCheckBranch.TrimStart().TrimEnd(), worksheet.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.ManagerCheckAmount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 14].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["ManagerCheckAmount"] = (existingCollectionReceipt.ManagerCheckAmount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 14].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.EWT.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["EWT"] = (existingCollectionReceipt.EWT.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 15].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.WVAT.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 16].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["WVAT"] = (existingCollectionReceipt.WVAT.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 16].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.Total.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 17].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                crChanges["Total"] = (existingCollectionReceipt.Total.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 17].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.IsCertificateUpload.ToString().ToUpper().TrimStart().TrimEnd() != worksheet.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                crChanges["IsCertificateUpload"] = (existingCollectionReceipt.IsCertificateUpload.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 18].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.F2306FilePath!.TrimStart().TrimEnd() != worksheet.Cells[row, 19].Text.TrimStart().TrimEnd())
                            {
                                crChanges["F2306FilePath"] = (existingCollectionReceipt.F2306FilePath.TrimStart().TrimEnd(), worksheet.Cells[row, 19].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.F2307FilePath!.TrimStart().TrimEnd() != worksheet.Cells[row, 20].Text.TrimStart().TrimEnd())
                            {
                                crChanges["F2307FilePath"] = (existingCollectionReceipt.F2307FilePath.TrimStart().TrimEnd(), worksheet.Cells[row, 20].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CreatedBy!.TrimStart().TrimEnd() != worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CreatedBy"] = (existingCollectionReceipt.CreatedBy.TrimStart().TrimEnd(), worksheet.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff".TrimStart().TrimEnd()) != worksheet.Cells[row, 22].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CreatedDate"] = (existingCollectionReceipt.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet.Cells[row, 22].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingCollectionReceipt.CancellationRemarks) ? "" : existingCollectionReceipt.CancellationRemarks.TrimStart().TrimEnd()) != worksheet.Cells[row, 23].Text.TrimStart().TrimEnd())
                            {
                                crChanges["CancellationRemarks"] = (existingCollectionReceipt.CancellationRemarks!.TrimStart().TrimEnd(), worksheet.Cells[row, 23].Text.TrimStart().TrimEnd());
                            }

                            var multipleSiId = existingCollectionReceipt.MultipleSIId != null
                                ? string.Join(", ", existingCollectionReceipt.MultipleSIId.Select(si => si.ToString()))
                                : null;
                            if (multipleSiId != null && multipleSiId.TrimStart().TrimEnd() != worksheet.Cells[row, 25].Text.TrimStart().TrimEnd())
                            {
                                var multipleSi = existingCollectionReceipt.MultipleSI != null
                                    ? string.Join(", ", existingCollectionReceipt.MultipleSI.Select(si => si.ToString()))
                                    : null;
                                if (multipleSi != null && multipleSi.TrimStart().TrimEnd() != worksheet.Cells[row, 24].Text.TrimStart().TrimEnd())
                                {
                                    crChanges["MultipleSI"] = (multipleSi.TrimStart().TrimEnd(), worksheet.Cells[row, 24].Text.TrimStart().TrimEnd());
                                }

                                if (multipleSiId.TrimStart().TrimEnd() != worksheet.Cells[row, 25].Text.TrimStart().TrimEnd())
                                {
                                    crChanges["MultipleSIId"] = (multipleSiId.TrimStart().TrimEnd(), worksheet.Cells[row, 25].Text.TrimStart().TrimEnd());
                                }

                                var siMultipleAmount = existingCollectionReceipt.SIMultipleAmount != null
                                    ? string.Join(" ", existingCollectionReceipt.SIMultipleAmount.Select(si => si.ToString("N4")))
                                    : null;
                                if (siMultipleAmount != null && siMultipleAmount.TrimStart().TrimEnd() != worksheet.Cells[row, 26].Text.TrimStart().TrimEnd())
                                {
                                    crChanges["SIMultipleAmount"] = (siMultipleAmount.TrimStart().TrimEnd(), worksheet.Cells[row, 26].Text.TrimStart().TrimEnd());
                                }

                                var multipleTransactionDate = existingCollectionReceipt.MultipleTransactionDate != null
                                    ? string.Join(", ", existingCollectionReceipt.MultipleTransactionDate.Select(multipleTransactionDate => multipleTransactionDate.ToString("yyyy-MM-dd")))
                                    : null;
                                if (multipleTransactionDate != null && multipleTransactionDate.TrimStart().TrimEnd() != worksheet.Cells[row, 27].Text.TrimStart().TrimEnd())
                                {
                                    crChanges["MultipleTransactionDate"] = (multipleTransactionDate.TrimStart().TrimEnd(), worksheet.Cells[row, 27].Text.TrimStart().TrimEnd());
                                }
                            }

                            if (existingCollectionReceipt.OriginalCustomerId.ToString()!.TrimStart().TrimEnd() != (worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 28].Text.TrimStart().TrimEnd()))
                            {
                                crChanges["OriginalCustomerId"] = (existingCollectionReceipt.OriginalCustomerId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 28].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 28].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.OriginalSalesInvoiceId.ToString()!.TrimStart().TrimEnd() != (worksheet.Cells[row, 29].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 29].Text.TrimStart().TrimEnd()))
                            {
                                crChanges["OriginalSalesInvoiceId"] = (existingCollectionReceipt.OriginalSalesInvoiceId.ToString(), worksheet.Cells[row, 29].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 29].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingCollectionReceipt.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet.Cells[row, 30].Text.TrimStart().TrimEnd())
                            {
                                crChanges["OriginalSeriesNumber"] = (existingCollectionReceipt.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet.Cells[row, 30].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.OriginalServiceInvoiceId.ToString()!.TrimStart().TrimEnd() != (worksheet.Cells[row, 31].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 31].Text.TrimStart().TrimEnd()))
                            {
                                crChanges["OriginalServiceInvoiceId"] = (existingCollectionReceipt.OriginalServiceInvoiceId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 31].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 31].Text.TrimStart().TrimEnd());
                            }

                            if (existingCollectionReceipt.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet.Cells[row, 32].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 32].Text.TrimStart().TrimEnd()))
                            {
                                crChanges["OriginalDocumentId"] = (existingCollectionReceipt.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 32].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 32].Text.TrimStart().TrimEnd());
                            }

                            if (crChanges.Any())
                            {
                                await _receiptRepo.LogChangesAsync(existingCollectionReceipt.OriginalDocumentId, crChanges, _userManager.GetUserName(this.User), existingCollectionReceipt.CollectionReceiptNo);
                            }

                            continue;
                        }
                        else
                        {
                            #region --Audit Trail Recording

                            if (!collectionReceipt.CreatedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(collectionReceipt.CreatedBy, $"Create new collection receipt# {collectionReceipt.CollectionReceiptNo}", "Collection Receipt", ipAddress!, collectionReceipt.CreatedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }
                            if (!collectionReceipt.PostedBy.IsNullOrEmpty())
                            {
                                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                                AuditTrail auditTrailBook = new(collectionReceipt.PostedBy, $"Posted collection receipt# {collectionReceipt.CollectionReceiptNo}", "Collection Receipt", ipAddress!, collectionReceipt.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        collectionReceipt.CustomerId = await _dbContext.Customers
                            .Where(c => c.OriginalCustomerId == collectionReceipt.OriginalCustomerId)
                            .Select(c => (int?)c.CustomerId)
                            .FirstOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Please upload the Excel file for the customer master file first.");

                        var getSi = await _dbContext.SalesInvoices
                            .Where(si => si.OriginalDocumentId == collectionReceipt.OriginalSalesInvoiceId)
                            .Select(si => new { si.SalesInvoiceId, SINo = si.SalesInvoiceNo })
                            .FirstOrDefaultAsync(cancellationToken);

                        collectionReceipt.SalesInvoiceId = getSi?.SalesInvoiceId;
                        collectionReceipt.SINo = getSi?.SINo;

                        var getSv = await _dbContext.ServiceInvoices
                            .Where(sv => sv.OriginalDocumentId == collectionReceipt.OriginalServiceInvoiceId)
                            .Select(sv => new { sv.ServiceInvoiceId, SVNo = sv.ServiceInvoiceNo })
                            .FirstOrDefaultAsync(cancellationToken);

                        collectionReceipt.ServiceInvoiceId = getSv?.ServiceInvoiceId;
                        collectionReceipt.SVNo = getSv?.SVNo;

                        if((getSv == null && !collectionReceipt.OriginalSalesInvoiceId.HasValue) &&
                           (getSi == null && !collectionReceipt.OriginalServiceInvoiceId.HasValue))
                        {
                            throw new InvalidOperationException("Please upload the Excel file for the sales invoice or service invoice first.");
                        }

                        foreach (var item in collectionReceipt.MultipleSIId)
                        {
                            if (item == 0)
                            {
                                collectionReceipt.MultipleSIId = null;
                            }
                        }
                        foreach (var item in collectionReceipt.SIMultipleAmount)
                        {
                            if (item == 0)
                            {
                                collectionReceipt.SIMultipleAmount = null;
                            }
                        }

                        await _dbContext.CollectionReceipts.AddAsync(collectionReceipt, cancellationToken);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion -- Collection Receipt Import --

                    #region -- Offsetting Import --

                    var offsetRowCount = worksheet2?.Dimension?.Rows ?? 0;

                    for (int offsetRow = 2; offsetRow <= offsetRowCount; offsetRow++)
                    {
                        if (worksheet2 == null || offsetRowCount == 0)
                        {
                            continue;
                        }

                        var offsettingRow = offsetRow;
                        var offsetting = new Offsetting
                        {
                            AccountNo = worksheet2.Cells[offsetRow, 1].Text,
                            Reference = worksheet2.Cells[offsetRow, 3].Text,
                            IsRemoved = bool.TryParse(worksheet2.Cells[offsetRow, 4].Text, out bool isRemoved) && isRemoved,
                            Amount = decimal.TryParse(worksheet2.Cells[offsetRow, 5].Text, out decimal amount) ? amount : 0,
                            CreatedBy = worksheet2.Cells[offsetRow, 6].Text,
                            CreatedDate = DateTime.TryParse(worksheet2.Cells[offsetRow, 7].Text, out DateTime createdDate) ? createdDate : default,
                            AccountTitle = worksheet2.Cells[offsetRow, 8].Text,
                            Source = await _dbContext.CollectionReceipts
                                .Where(cr => cr.OriginalSeriesNumber == worksheet2.Cells[offsettingRow, 2].Text)
                                .Select(cr => cr.CollectionReceiptNo)
                                .FirstOrDefaultAsync(cancellationToken) ?? String.Empty
                        };

                        await _dbContext.Offsettings.AddAsync(offsetting, cancellationToken);
                    }
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    var checkChangesOfRecord = await _dbContext.ImportExportLogs
                        .Where(iel => iel.Action == string.Empty).ToListAsync(cancellationToken);
                    if (checkChangesOfRecord.Any())
                    {
                        TempData["importChanges"] = "";
                    }
                    #endregion -- Offsetting Import --
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
                }
                catch (InvalidOperationException ioe)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["warning"] = ioe.Message;
                    return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(CollectionIndex), new { view = DynamicView.CollectionReceipt });
        }

        #endregion -- import xlsx record --
    }
}
