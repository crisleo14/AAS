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
    public class SalesInvoiceController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly SalesInvoiceRepo _salesInvoiceRepo;

        private readonly ILogger<HomeController> _logger;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly InventoryRepo _inventoryRepo;

        private readonly GeneralRepo _generalRepo;

        public SalesInvoiceController(ILogger<HomeController> logger, ApplicationDbContext dbContext, SalesInvoiceRepo salesInvoiceRepo, UserManager<IdentityUser> userManager, InventoryRepo inventoryRepo, GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _salesInvoiceRepo = salesInvoiceRepo;
            _logger = logger;
            this._userManager = userManager;
            _inventoryRepo = inventoryRepo;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var salesInvoices = await _salesInvoiceRepo.GetSalesInvoicesAsync(cancellationToken);

            if (view == nameof(DynamicView.SalesInvoice))
            {
                return View("ImportExportIndex", salesInvoices);
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetSalesInvoices([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var salesInvoices = await _salesInvoiceRepo.GetSalesInvoicesAsync(cancellationToken);
                // Search filter
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();

                    salesInvoices = salesInvoices
                        .Where(s =>
                            s.SalesInvoiceNo!.ToLower().Contains(searchValue) ||
                            s.Customer!.CustomerName.ToLower().Contains(searchValue) ||
                            s.Customer.CustomerTerms.ToLower().Contains(searchValue) ||
                            s.Product!.ProductCode!.ToLower().Contains(searchValue) ||
                            s.Product.ProductName.ToLower().Contains(searchValue) ||
                            s.Status.ToLower().Contains(searchValue) ||
                            s.TransactionDate.ToString("MMM dd, yyyy").ToLower().Contains(searchValue) ||
                            s.Quantity.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            s.UnitPrice.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            s.Amount.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            s.Remarks.ToLower().Contains(searchValue) ||
                            s.CreatedBy!.ToLower().Contains(searchValue)
                            )
                        .ToList();
                }

                // Sorting
                if (parameters.Order != null && parameters.Order.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";

                    salesInvoices = salesInvoices
                        .AsQueryable()
                        .OrderBy($"{columnName} {sortDirection}")
                        .ToList();
                }

                var totalRecords = salesInvoices.Count();

                var pagedData = salesInvoices
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
        public async Task<IActionResult> GetAllSalesInvoiceIds(CancellationToken cancellationToken)
        {
            var invoiceIds = await _dbContext.SalesInvoices
                                     .Select(invoice => invoice.SalesInvoiceId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(invoiceIds);
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var viewModel = new SalesInvoice
            {
                Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(c => new SelectListItem
                    {
                        Value = c.CustomerId.ToString(),
                        Text = c.CustomerName
                    })
                    .ToListAsync(cancellationToken),
                Products = await _dbContext.Products
                    .OrderBy(p => p.ProductId)
                    .Select(p => new SelectListItem
                    {
                        Value = p.ProductId.ToString(),
                        Text = p.ProductName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SalesInvoice sales, CancellationToken cancellationToken)
        {
            sales.Customers = await _dbContext.Customers
                .OrderBy(c => c.CustomerId)
                .Select(c => new SelectListItem
                {
                    Value = c.CustomerId.ToString(),
                    Text = c.CustomerName
                })
                .ToListAsync(cancellationToken);
            sales.Products = await _dbContext.Products
                .OrderBy(p => p.ProductCode)
                .Select(p => new SelectListItem
                {
                    Value = p.ProductId.ToString(),
                    Text = p.ProductName
                })
                .ToListAsync(cancellationToken);
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    #region -- Validating Series --

                    var generateSiNo = await _salesInvoiceRepo.GenerateSINo(cancellationToken);
                    var getLastNumber = long.Parse(generateSiNo.Substring(2));

                    if (getLastNumber > 9999999999)
                    {
                        TempData["error"] = "You reach the maximum Series Number";
                        return View(sales);
                    }
                    var totalRemainingSeries = 9999999999 - getLastNumber;
                    if (getLastNumber >= 9999999899)
                    {
                        TempData["warning"] = $"Sales Invoice created successfully, Warning {totalRemainingSeries} series number remaining";
                    }
                    else
                    {
                        TempData["success"] = "Sales Invoice created successfully";
                    }

                    #endregion -- Validating Series --

                    #region -- Saving Default Entries --

                    var existingCustomers = await _dbContext.Customers
                                                   .FirstOrDefaultAsync(si => si.CustomerId == sales.CustomerId, cancellationToken);

                    sales.CreatedBy = _userManager.GetUserName(this.User);
                    sales.SalesInvoiceNo = generateSiNo;
                    sales.Amount = sales.Quantity * sales.UnitPrice;
                    sales.DueDate = _salesInvoiceRepo.ComputeDueDateAsync(existingCustomers!.CustomerTerms, sales.TransactionDate, cancellationToken);
                    if (sales.Amount >= sales.Discount)
                    {
                        await _dbContext.AddAsync(sales, cancellationToken);
                    }
                    else
                    {
                        TempData["error"] = "Please input below or exact amount based on the Sales Invoice";
                        return View(sales);
                    }

                    #region --Audit Trail Recording

                    if (sales.OriginalSeriesNumber.IsNullOrEmpty() && sales.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(sales.CreatedBy!, $"Create new invoice# {sales.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return RedirectToAction(nameof(Index));

                    #endregion -- Saving Default Entries --
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return View(sales);
                }
            }
            else
            {
                ModelState.AddModelError("", "The information you submitted is not valid!");
                return View(sales);
            }
        }

        [HttpGet]
        public async Task<JsonResult> GetCustomerDetails(int customerId, CancellationToken cancellationToken)
        {
            var customer = await _dbContext.Customers.FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
            if (customer != null)
            {
                return Json(new
                {
                    SoldTo = customer.CustomerName,
                    Address = customer.CustomerAddress,
                    TinNo = customer.CustomerTin,
                    customer.BusinessStyle,
                    Terms = customer.CustomerTerms,
                    customer.CustomerType,
                    customer.WithHoldingTax
                });
            }
            return Json(null); // Return null if no matching customer is found
        }

        [HttpGet]
        public async Task<JsonResult> GetProductDetails(int productId, CancellationToken cancellationToken)
        {
            var product = await _dbContext.Products.FirstOrDefaultAsync(c => c.ProductId == productId, cancellationToken);
            if (product != null)
            {
                return Json(new
                {
                    product.ProductName, product.ProductUnit
                });
            }
            return Json(null); // Return null if no matching product is found
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            try
            {
                var salesInvoice = await _salesInvoiceRepo.FindSalesInvoice(id, cancellationToken);
                salesInvoice.Customers = await _dbContext.Customers
                .OrderBy(c => c.CustomerId)
                .Select(c => new SelectListItem
                {
                    Value = c.CustomerId.ToString(),
                    Text = c.CustomerName
                })
                .ToListAsync(cancellationToken);
                salesInvoice.Products = await _dbContext.Products
                .OrderBy(p => p.ProductId)
                .Select(p => new SelectListItem
                {
                    Value = p.ProductId.ToString(),
                    Text = p.ProductName
                })
                .ToListAsync(cancellationToken);

                return View(salesInvoice);
            }
            catch (Exception ex)
            {
                // Handle other exceptions, log them, and return an error response.
                _logger.LogError(ex, "An error occurred.");
                return StatusCode(500, "An error occurred. Please try again later.");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(SalesInvoice model, CancellationToken cancellationToken)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var existingModel = await _salesInvoiceRepo.FindSalesInvoice(model.SalesInvoiceId, cancellationToken);
            try
            {
                if (ModelState.IsValid)
                {
                    #region -- Saving Default Enries --

                    existingModel.CustomerId = model.CustomerId;
                    existingModel.TransactionDate = model.TransactionDate;
                    existingModel.OtherRefNo = model.OtherRefNo;
                    existingModel.Quantity = model.Quantity;
                    existingModel.UnitPrice = model.UnitPrice;
                    existingModel.Remarks = model.Remarks;
                    existingModel.Discount = model.Discount;
                    existingModel.Amount = model.Quantity * model.UnitPrice;
                    existingModel.ProductId = model.ProductId;
                    existingModel.DueDate = _salesInvoiceRepo.ComputeDueDateAsync(existingModel.Customer!.CustomerTerms, existingModel.TransactionDate, cancellationToken);

                    if (existingModel.Amount >= model.Discount)
                    {
                        #region --Audit Trail Recording

                        if (existingModel.OriginalSeriesNumber.IsNullOrEmpty() && existingModel.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            var modifiedBy = _userManager.GetUserName(this.User);
                            AuditTrail auditTrailBook = new(modifiedBy!, $"Edited invoice# {existingModel.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording
                    }
                    else
                    {
                        existingModel.Customers = await _dbContext.Customers
                            .OrderBy(c => c.CustomerId)
                            .Select(c => new SelectListItem
                            {
                                Value = c.CustomerId.ToString(),
                                Text = c.CustomerName
                            })
                            .ToListAsync(cancellationToken);
                        existingModel.Products = await _dbContext.Products
                            .OrderBy(p => p.ProductId)
                            .Select(p => new SelectListItem
                            {
                                Value = p.ProductId.ToString(),
                                Text = p.ProductName
                            })
                            .ToListAsync(cancellationToken);
                        TempData["error"] = "Please input below or exact amount based unit price multiply quantity";
                        return View(existingModel);
                    }

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        // Save the changes to the database
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Sales Invoice updated successfully";
                        return RedirectToAction(nameof(Index)); // Redirect to a success page or the index page
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }

                    #endregion -- Saving Default Enries --
                }

                existingModel.Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(c => new SelectListItem
                    {
                        Value = c.CustomerId.ToString(),
                        Text = c.CustomerName
                    })
                    .ToListAsync(cancellationToken);
                existingModel.Products = await _dbContext.Products
                    .OrderBy(p => p.ProductId)
                    .Select(p => new SelectListItem
                    {
                        Value = p.ProductId.ToString(),
                        Text = p.ProductName
                    })
                    .ToListAsync(cancellationToken);

                ModelState.AddModelError("", "The information you submitted is not valid!");
                return View(existingModel);
            }
            catch (Exception ex)
            {
                existingModel.Customers = await _dbContext.Customers
                    .OrderBy(c => c.CustomerId)
                    .Select(c => new SelectListItem
                    {
                        Value = c.CustomerId.ToString(),
                        Text = c.CustomerName
                    })
                    .ToListAsync(cancellationToken);
                existingModel.Products = await _dbContext.Products
                    .OrderBy(p => p.ProductId)
                    .Select(p => new SelectListItem
                    {
                        Value = p.ProductId.ToString(),
                        Text = p.ProductName
                    })
                    .ToListAsync(cancellationToken);
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return View(existingModel);
            }
        }

        public async Task<IActionResult> PrintInvoice(int id, CancellationToken cancellationToken)
        {
            var sales = await _salesInvoiceRepo.FindSalesInvoice(id, cancellationToken);
            return View(sales);
        }

        public async Task<IActionResult> PrintedInvoice(int id, CancellationToken cancellationToken)
        {
            var sales = await _salesInvoiceRepo.FindSalesInvoice(id, cancellationToken);
            if (!sales.IsPrinted)
            {
                sales.IsPrinted = true;

                #region --Audit Trail Recording

                if (sales.OriginalSeriesNumber.IsNullOrEmpty() && sales.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var printedBy = _userManager.GetUserName(this.User);
                    AuditTrail auditTrailBook = new(printedBy!, $"Printed original copy of invoice# {sales.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(PrintInvoice), new { id });
        }

        public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
        {
            var model = await _salesInvoiceRepo.FindSalesInvoice(id, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (!model.IsPosted)
                {
                    model.IsPosted = true;
                    model.PostedBy = _userManager.GetUserName(this.User);
                    model.PostedDate = DateTime.Now;

                    #region --Sales Book Recording

                    var salesBook = new SalesBook
                    {
                        TransactionDate = model.TransactionDate,
                        SerialNo = model.SalesInvoiceNo!,
                        SoldTo = model.Customer!.CustomerName,
                        TinNo = model.Customer.CustomerTin,
                        Address = model.Customer.CustomerAddress,
                        Description = model.Product!.ProductName,
                        Amount = model.Amount - model.Discount
                    };

                    switch (model.Customer.CustomerType)
                    {
                        case CS.VatType_Vatable:
                            salesBook.VatableSales = _generalRepo.ComputeNetOfVat(salesBook.Amount);
                            salesBook.VatAmount = _generalRepo.ComputeVatAmount(salesBook.VatableSales);
                            salesBook.NetSales = salesBook.VatableSales - salesBook.Discount;
                            break;
                        case CS.VatType_Exempt:
                            salesBook.VatExemptSales = salesBook.Amount;
                            salesBook.NetSales = salesBook.VatExemptSales - salesBook.Discount;
                            break;
                        default:
                            salesBook.ZeroRated = salesBook.Amount;
                            salesBook.NetSales = salesBook.ZeroRated - salesBook.Discount;
                            break;
                    }

                    salesBook.Discount = model.Discount;
                    salesBook.CreatedBy = model.CreatedBy;
                    salesBook.CreatedDate = model.CreatedDate;
                    salesBook.DueDate = model.DueDate;
                    salesBook.DocumentId = model.SalesInvoiceId;

                    await _dbContext.SalesBooks.AddAsync(salesBook, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    #endregion --Sales Book Recording

                    #region --General Ledger Book Recording

                    decimal netDiscount = model.Amount - model.Discount;
                    decimal netOfVatAmount = model.Customer.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeNetOfVat(netDiscount) : netDiscount;
                    decimal vatAmount = model.Customer.CustomerType == CS.VatType_Vatable ? _generalRepo.ComputeVatAmount(netOfVatAmount) : 0;
                    decimal withHoldingTaxAmount = model.Customer.WithHoldingTax ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.01m) : 0;
                    decimal withHoldingVatAmount = model.Customer.WithHoldingVat ? _generalRepo.ComputeEwtAmount(netOfVatAmount, 0.05m) : 0;

                    var accountTitlesDto = await _generalRepo.GetListOfAccountTitleDto(cancellationToken);
                    var arTradeReceivableTitle = accountTitlesDto.Find(c => c.AccountNumber == "101020100") ?? throw new ArgumentException("Account number: '101020100', Account title: 'AR-Trade Receivable' not found.");
                    var arTradeCwt = accountTitlesDto.Find(c => c.AccountNumber == "101020200") ?? throw new ArgumentException("Account number: '101020200', Account title: 'AR-Trade Receivable - Creditable Withholding Tax' not found.");
                    var arTradeCwv = accountTitlesDto.Find(c => c.AccountNumber == "101020300") ?? throw new ArgumentException("Account number: '101020300', Account title: 'AR-Trade Receivable - Creditable Withholding Vat' not found.");
                    var (salesAcctNo, _) = _generalRepo.GetSalesAccountTitle(model.Product.ProductCode!);
                    var salesTitle = accountTitlesDto.Find(c => c.AccountNumber == salesAcctNo) ?? throw new ArgumentException($"Account title '{salesAcctNo}' not found.");
                    var vatOutputTitle = accountTitlesDto.Find(c => c.AccountNumber == "201030100") ?? throw new ArgumentException("Account number: '201030100', Account title: 'Vat - Output' not found.");


                    var ledgers = new List<GeneralLedgerBook>
                    {
                        new GeneralLedgerBook
                        {
                            Date = model.TransactionDate,
                            Reference = model.SalesInvoiceNo!,
                            Description = model.Product.ProductName,
                            AccountNo = arTradeReceivableTitle.AccountNumber,
                            AccountTitle = arTradeReceivableTitle.AccountName,
                            Debit = netDiscount - (withHoldingTaxAmount + withHoldingVatAmount),
                            Credit = 0,
                            CreatedBy = model.CreatedBy,
                            CreatedDate = model.CreatedDate
                        }
                    };

                    if (withHoldingTaxAmount > 0)
                    {
                        ledgers.Add(
                            new GeneralLedgerBook
                            {
                                Date = model.TransactionDate,
                                Reference = model.SalesInvoiceNo!,
                                Description = model.Product.ProductName,
                                AccountNo = arTradeCwt.AccountNumber,
                                AccountTitle = arTradeCwt.AccountName,
                                Debit = withHoldingTaxAmount,
                                Credit = 0,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }
                    if (withHoldingVatAmount > 0)
                    {
                        ledgers.Add(
                            new GeneralLedgerBook
                            {
                                Date = model.TransactionDate,
                                Reference = model.SalesInvoiceNo!,
                                Description = model.Product.ProductName,
                                AccountNo = arTradeCwv.AccountNumber,
                                AccountTitle = arTradeCwv.AccountName,
                                Debit = withHoldingVatAmount,
                                Credit = 0,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }
                    ledgers.Add(
                        new GeneralLedgerBook
                        {
                            Date = model.TransactionDate,
                            Reference = model.SalesInvoiceNo!,
                            Description = model.Product.ProductName,
                            AccountNo = salesTitle.AccountNumber,
                            AccountTitle = salesTitle.AccountName,
                            Debit = 0,
                            Credit = netOfVatAmount,
                            CreatedBy = model.CreatedBy,
                            CreatedDate = model.CreatedDate
                        }
                    );
                    if (vatAmount > 0)
                    {
                        ledgers.Add(
                            new GeneralLedgerBook
                            {
                                Date = model.TransactionDate,
                                Reference = model.SalesInvoiceNo!,
                                Description = model.Product.ProductName,
                                AccountNo = vatOutputTitle.AccountNumber,
                                AccountTitle = vatOutputTitle.AccountName,
                                Debit = 0,
                                Credit = vatAmount,
                                CreatedBy = model.CreatedBy,
                                CreatedDate = model.CreatedDate
                            }
                        );
                    }

                    if (!_generalRepo.IsJournalEntriesBalanced(ledgers))
                    {
                        throw new ArgumentException("Debit and Credit is not equal, check your entries.");
                    }

                    await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledgers, cancellationToken);

                    #endregion --General Ledger Book Recording

                    #region--Inventory Recording

                    await _inventoryRepo.AddSalesToInventoryAsync(model, User, cancellationToken);

                    #endregion

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.PostedBy!, $"Posted invoice# {model.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Sales Invoice has been Posted.";
                    return RedirectToAction(nameof(PrintInvoice), new { id });
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

        public async Task<IActionResult> Void(int id, CancellationToken cancellationToken)
        {
            var model = await _dbContext.SalesInvoices.FirstOrDefaultAsync(x => x.SalesInvoiceId == id, cancellationToken);

            var existingInventory = await _dbContext.Inventories
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Reference == model!.SalesInvoiceNo, cancellationToken: cancellationToken);

            if (model != null && existingInventory != null)
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

                        model.IsVoided = true;
                        model.VoidedBy = _userManager.GetUserName(this.User);
                        model.VoidedDate = DateTime.Now;

                        await _generalRepo.RemoveRecords<SalesBook>(sb => sb.SerialNo == model.SalesInvoiceNo, cancellationToken);
                        await _generalRepo.RemoveRecords<GeneralLedgerBook>(gl => gl.Reference == model.SalesInvoiceNo, cancellationToken);
                        await _inventoryRepo.VoidInventory(existingInventory, cancellationToken);

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.VoidedBy!, $"Voided invoice# {model.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Sales Invoice has been Voided.";
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
                return RedirectToAction(nameof(Index));
            }

            return NotFound();
        }

        public async Task<IActionResult> Cancel(int id, string cancellationRemarks, CancellationToken cancellationToken)
        {
            var model = await _dbContext.SalesInvoices.FirstOrDefaultAsync(x => x.SalesInvoiceId == id, cancellationToken);
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
                        model.Status = "Cancelled";
                        model.CancellationRemarks = cancellationRemarks;

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.CanceledBy!, $"Cancelled invoice# {model.SalesInvoiceNo}", "Sales Invoice", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Sales Invoice has been Cancelled.";
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

        public async Task<IActionResult> GetPOs(int productId, CancellationToken cancellationToken)
        {
            var purchaseOrders = await _dbContext.PurchaseOrders
                .Where(po => po.ProductId == productId && po.QuantityReceived != 0 && po.IsPosted)
                .ToListAsync(cancellationToken);

            if (purchaseOrders.Count > 0)
            {
                var poList = purchaseOrders.Select(po => new { Id = po.PurchaseOrderId, PONumber = po.PurchaseOrderNo }).ToList();
                return Json(poList);
            }

            return Json(null);
        }

        public IActionResult GetRRs(int purchaseOrderId)
        {
            var rrs = _dbContext.ReceivingReports
                              .Where(rr => rr.POId == purchaseOrderId && rr.ReceivedDate != null && rr.IsPosted)
                              .Select(rr => new
                              {
                                  rr.ReceivingReportId,
                                  RRNo = rr.ReceivingReportNo,
                                  rr.ReceivedDate
                              })
                              .ToList();

            return Json(rrs);
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
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
                    }

                    if (worksheet.ToString() != nameof(DynamicView.SalesInvoice))
                    {
                        TempData["error"] = "The Excel file is not related to sales invoice.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var siDictionary = new Dictionary<string, bool>();
                    var invoiceList = await _dbContext
                        .SalesInvoices
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++) // Assuming the first row is the header
                    {
                        var invoice = new SalesInvoice
                        {
                            SalesInvoiceNo = worksheet.Cells[row, 21].Text,
                            OtherRefNo = worksheet.Cells[row, 1].Text,
                            Quantity = decimal.TryParse(worksheet.Cells[row, 2].Text, out decimal quantity)
                                ? quantity
                                : 0,
                            UnitPrice = decimal.TryParse(worksheet.Cells[row, 3].Text, out decimal unitPrice)
                                ? unitPrice
                                : 0,
                            Amount =
                                decimal.TryParse(worksheet.Cells[row, 4].Text, out decimal amount) ? amount : 0,
                            Remarks = worksheet.Cells[row, 5].Text,
                            Status = worksheet.Cells[row, 6].Text,
                            TransactionDate =
                                DateOnly.TryParse(worksheet.Cells[row, 7].Text, out DateOnly transactionDate)
                                    ? transactionDate
                                    : default,
                            Discount = decimal.TryParse(worksheet.Cells[row, 8].Text, out decimal discount)
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
                            DueDate = DateOnly.TryParse(worksheet.Cells[row, 13].Text, out DateOnly dueDate)
                                ? dueDate
                                : default,
                            CreatedBy = worksheet.Cells[row, 14].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 15].Text, out DateTime createdDate)
                                ? createdDate
                                : default,
                            PostedBy = worksheet.Cells[row, 23].Text,
                            PostedDate = DateTime.TryParse(worksheet.Cells[row, 24].Text, out DateTime postedDate)
                                ? postedDate
                                : default,
                            CancellationRemarks = worksheet.Cells[row, 16].Text != ""
                                ? worksheet.Cells[row, 16].Text
                                : null,
                            OriginalCustomerId = int.TryParse(worksheet.Cells[row, 18].Text, out int customerId)
                                ? customerId
                                : 0,
                            OriginalProductId = int.TryParse(worksheet.Cells[row, 20].Text, out int productId)
                                ? productId
                                : 0,
                            OriginalSeriesNumber = worksheet.Cells[row, 21].Text,
                            OriginalDocumentId =
                                int.TryParse(worksheet.Cells[row, 22].Text, out int originalDocumentId)
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
                            var existingSi = await _dbContext.SalesInvoices.FirstOrDefaultAsync(si => si.OriginalDocumentId == invoice.OriginalDocumentId, cancellationToken);

                            if (existingSi!.SalesInvoiceNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                siChanges["SiNo"] = (existingSi.SalesInvoiceNo.TrimStart().TrimEnd(), worksheet.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.OriginalCustomerId.ToString()!.TrimStart().TrimEnd() != worksheet.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalCustomerId"] = (existingSi.OriginalCustomerId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 18].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.OriginalProductId.ToString()!.TrimStart().TrimEnd() != worksheet.Cells[row, 20].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalProductId"] = (existingSi.OriginalProductId.ToString()!.TrimStart().TrimEnd(), worksheet.Cells[row, 20].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.OtherRefNo.TrimStart().TrimEnd() != worksheet.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OtherRefNo"] = (existingSi.OtherRefNo.TrimStart().TrimEnd(), worksheet.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.Quantity.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 2].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Quantity"] = (existingSi.Quantity.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 2].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSi.UnitPrice.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["UnitPrice"] = (existingSi.UnitPrice.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSi.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Amount"] = (existingSi.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSi.Remarks.TrimStart().TrimEnd() != worksheet.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                siChanges["Remarks"] = (existingSi.Remarks.TrimStart().TrimEnd(), worksheet.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.Status.TrimStart().TrimEnd() != worksheet.Cells[row, 6].Text.TrimStart().TrimEnd())
                            {
                                siChanges["Status"] = (existingSi.Status.TrimStart().TrimEnd(), worksheet.Cells[row, 6].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 7].Text.TrimStart().TrimEnd())
                            {
                                siChanges["TransactionDate"] = (existingSi.TransactionDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 7].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.Discount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                siChanges["Discount"] = (existingSi.Discount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingSi.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                siChanges["DueDate"] = (existingSi.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.CreatedBy!.TrimStart().TrimEnd() != worksheet.Cells[row, 14].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CreatedBy"] = (existingSi.CreatedBy.TrimStart().TrimEnd(), worksheet.Cells[row, 14].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet.Cells[row, 15].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CreatedDate"] = (existingSi.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet.Cells[row, 15].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingSi.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingSi.CancellationRemarks.TrimStart().TrimEnd()) != worksheet.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                siChanges["CancellationRemarks"] = (existingSi.CancellationRemarks!.TrimStart().TrimEnd(), worksheet.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalSeriesNumber"] = (existingSi.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingSi.OriginalDocumentId.ToString().TrimStart().TrimEnd() != worksheet.Cells[row, 22].Text.TrimStart().TrimEnd())
                            {
                                siChanges["OriginalDocumentId"] = (existingSi.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 22].Text.TrimStart().TrimEnd());
                            }

                            if (siChanges.Any())
                            {
                                await _salesInvoiceRepo.LogChangesAsync(existingSi.OriginalDocumentId, siChanges, _userManager.GetUserName(this.User), existingSi.SalesInvoiceNo);
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
                    await transaction.CommitAsync(cancellationToken);

                    var checkChangesOfRecord = await _dbContext.ImportExportLogs
                        .Where(iel => iel.Action == string.Empty).ToListAsync(cancellationToken);
                    if (checkChangesOfRecord.Any())
                    {
                        TempData["importChanges"] = "";
                    }
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
                }
                catch (InvalidOperationException ioe)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["warning"] = ioe.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.SalesInvoice });
        }

        #endregion -- import xlsx record --
    }
}
