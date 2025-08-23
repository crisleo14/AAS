using System.Globalization;
using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
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
    public class ReceivingReportController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly ReceivingReportRepo _receivingReportRepo;

        private readonly PurchaseOrderRepo _purchaseOrderRepo;

        private readonly GeneralRepo _generalRepo;

        private readonly InventoryRepo _inventoryRepo;

        public ReceivingReportController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, ReceivingReportRepo receivingReportRepo, GeneralRepo generalRepo, InventoryRepo inventoryRepo, PurchaseOrderRepo purchaseOrderRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _receivingReportRepo = receivingReportRepo;
            _purchaseOrderRepo = purchaseOrderRepo;
            _generalRepo = generalRepo;
            _inventoryRepo = inventoryRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var rr = await _receivingReportRepo.GetReceivingReportsAsync(cancellationToken);

            if (view == nameof(DynamicView.ReceivingReport))
            {
                return View("ImportExportIndex", rr);
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GetReceivingReports([FromForm] DataTablesParameters parameters, CancellationToken cancellationToken)
        {
            try
            {
                var receivingReports = await _receivingReportRepo.GetReceivingReportsAsync(cancellationToken);
                // Search filter
                if (!string.IsNullOrEmpty(parameters.Search.Value))
                {
                    var searchValue = parameters.Search.Value.ToLower();
                    receivingReports = receivingReports
                        .Where(rr =>
                            rr.ReceivingReportNo?.ToLower().Contains(searchValue) == true ||
                            rr.Date.ToString("MMM dd, yyyy").ToLower().Contains(searchValue) ||
                            rr.PONo?.ToLower().Contains(searchValue) == true ||
                            rr.QuantityDelivered.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            rr.QuantityReceived.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            rr.Amount.ToString(CultureInfo.InvariantCulture).Contains(searchValue) ||
                            rr.Remarks.ToString().ToLower().Contains(searchValue) ||
                            rr.CreatedBy!.ToLower().Contains(searchValue)
                            )
                        .ToList();
                }
                // Sorting
                if (parameters.Order != null && parameters.Order.Count > 0)
                {
                    var orderColumn = parameters.Order[0];
                    var columnName = parameters.Columns[orderColumn.Column].Data;
                    var sortDirection = orderColumn.Dir.ToLower() == "asc" ? "ascending" : "descending";
                    receivingReports = receivingReports
                        .AsQueryable()
                        .OrderBy($"{columnName} {sortDirection}")
                        .ToList();
                }
                var totalRecords = receivingReports.Count();
                var pagedData = receivingReports
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
        public async Task<IActionResult> GetAllReceivingReportIds(CancellationToken cancellationToken)
        {
            var receivingReportIds = await _dbContext.ReceivingReports
                                     .Select(rr => rr.ReceivingReportId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(receivingReportIds);
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var viewModel = new ReceivingReport
            {
                PurchaseOrders = await _dbContext.PurchaseOrders
                    .Where(po => !po.IsReceived && po.IsPosted && !po.IsClosed)
                    .Select(po => new SelectListItem
                    {
                        Value = po.PurchaseOrderId.ToString(),
                        Text = po.PurchaseOrderNo
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReceivingReport model, CancellationToken cancellationToken)
        {
            model.PurchaseOrders = await _dbContext.PurchaseOrders
                .Where(po => !po.IsReceived && po.IsPosted)
                .Select(po => new SelectListItem
                {
                    Value = po.PurchaseOrderId.ToString(),
                    Text = po.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);
            if (ModelState.IsValid)
            {
                #region --Retrieve PO

                var existingPo = await _dbContext
                            .PurchaseOrders
                            .Include(po => po.Supplier)
                            .Include(po => po.Product)
                            .FirstOrDefaultAsync(po => po.PurchaseOrderId == model.POId, cancellationToken);

                #endregion --Retrieve PO

                var totalAmountRr = existingPo!.Quantity - existingPo.QuantityReceived;

                if (model.QuantityDelivered > totalAmountRr)
                {
                    TempData["error"] = "Input is exceed to remaining quantity delivered";
                    return View(model);
                }

                #region --Validating Series

                var generatedRr = await _receivingReportRepo.GenerateRRNo(cancellationToken);
                var getLastNumber = long.Parse(generatedRr.Substring(2));

                if (getLastNumber > 9999999999)
                {
                    TempData["error"] = "You reach the maximum Series Number";
                    return View(model);
                }
                var totalRemainingSeries = 9999999999 - getLastNumber;
                if (getLastNumber >= 9999999899)
                {
                    TempData["warning"] = $"Receiving Report created successfully, Warning {totalRemainingSeries} series number remaining";
                }
                else
                {
                    TempData["success"] = "Receiving Report created successfully";
                }

                #endregion --Validating Series

                model.ReceivingReportNo = generatedRr;
                model.CreatedBy = User.Identity!.Name;
                model.GainOrLoss = model.QuantityReceived - model.QuantityDelivered;
                model.PONo = await _receivingReportRepo.GetPONoAsync(model.POId, cancellationToken);
                model.DueDate = await _receivingReportRepo.ComputeDueDateAsync(model.POId, model.Date, cancellationToken);

                model.Amount = model.QuantityReceived * existingPo.Price;

                #region --Audit Trail Recording

                if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    AuditTrail auditTrailBook = new(model.CreatedBy!, $"Create new receiving report# {model.ReceivingReportNo}", "Receiving Report", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                await _dbContext.AddAsync(model, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                return RedirectToAction(nameof(Index));
            }

            ModelState.AddModelError("", "The information you submitted is not valid!");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || !_dbContext.ReceivingReports.Any())
            {
                return NotFound();
            }

            var receivingReport = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == id, cancellationToken);
            if (receivingReport == null)
            {
                return NotFound();
            }

            receivingReport.PurchaseOrders = await _dbContext.PurchaseOrders
                .Select(s => new SelectListItem
                {
                    Value = s.PurchaseOrderId.ToString(),
                    Text = s.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);

            return View(receivingReport);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(ReceivingReport model, CancellationToken cancellationToken)
        {
            var existingModel = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == model.ReceivingReportId, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (ModelState.IsValid)
                {
                    if (existingModel == null)
                    {
                        return NotFound();
                    }

                    #region --Retrieve PO

                    var po = await _dbContext
                                .PurchaseOrders
                                .Include(po => po.Supplier)
                                .Include(po => po.Product)
                                .FirstOrDefaultAsync(po => po.PurchaseOrderId == model.POId, cancellationToken);

                    #endregion --Retrieve PO

                    var totalAmountRr = po!.Quantity - po.QuantityReceived;

                    if (model.QuantityDelivered > totalAmountRr && !existingModel.IsPosted)
                    {
                        TempData["error"] = "Input is exceed to remaining quantity delivered";
                        existingModel.PurchaseOrders = await _dbContext.PurchaseOrders
                            .Select(s => new SelectListItem
                            {
                                Value = s.PurchaseOrderId.ToString(),
                                Text = s.PurchaseOrderNo
                            })
                            .ToListAsync(cancellationToken);
                        return View(existingModel);
                    }

                    existingModel.Date = model.Date;
                    existingModel.POId = model.POId;
                    existingModel.PONo = await _receivingReportRepo.GetPONoAsync(model.POId, cancellationToken);
                    existingModel.DueDate = await _receivingReportRepo.ComputeDueDateAsync(model.POId, model.Date, cancellationToken);
                    existingModel.SupplierInvoiceNumber = model.SupplierInvoiceNumber;
                    existingModel.SupplierInvoiceDate = model.SupplierInvoiceDate;
                    existingModel.TruckOrVessels = model.TruckOrVessels;
                    existingModel.QuantityDelivered = model.QuantityDelivered;
                    existingModel.QuantityReceived = model.QuantityReceived;
                    existingModel.GainOrLoss = model.QuantityReceived - model.QuantityDelivered;
                    existingModel.OtherRef = model.OtherRef;
                    existingModel.Remarks = model.Remarks;
                    existingModel.ReceivedDate = model.ReceivedDate;
                    existingModel.Amount = model.QuantityReceived * po.Price;

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (existingModel.OriginalSeriesNumber.IsNullOrEmpty() && existingModel.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(existingModel.CreatedBy!, $"Edited receiving report# {existingModel.ReceivingReportNo}", "Receiving Report", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        TempData["success"] = "Receiving Report updated successfully";
                        await transaction.CommitAsync(cancellationToken);
                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                existingModel!.PurchaseOrders = await _dbContext.PurchaseOrders
                    .Select(s => new SelectListItem
                    {
                        Value = s.PurchaseOrderId.ToString(),
                        Text = s.PurchaseOrderNo
                    })
                    .ToListAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return View(existingModel);
            }

            existingModel!.PurchaseOrders = await _dbContext.PurchaseOrders
                .Select(s => new SelectListItem
                {
                    Value = s.PurchaseOrderId.ToString(),
                    Text = s.PurchaseOrderNo
                })
                .ToListAsync(cancellationToken);
            return View(existingModel);
        }

        [HttpGet]
        public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
        {
            if (id == 0 || !_dbContext.ReceivingReports.Any())
            {
                return NotFound();
            }

            var receivingReport = await _receivingReportRepo.FindRR(id, cancellationToken);

            return View(receivingReport);
        }

        public async Task<IActionResult> Printed(int id, CancellationToken cancellationToken)
        {
            var rr = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == id, cancellationToken);
            if (rr != null && !rr.IsPrinted)
            {

                #region --Audit Trail Recording

                if (rr.OriginalSeriesNumber.IsNullOrEmpty() && rr.OriginalDocumentId == 0)
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var printedBy = _userManager.GetUserName(this.User);
                    AuditTrail auditTrailBook = new(printedBy!, $"Printed original copy of rr# {rr.ReceivingReportNo}", "Receiving Report", ipAddress!);
                    await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                }

                #endregion --Audit Trail Recording

                rr.IsPrinted = true;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            return RedirectToAction(nameof(Print), new { id });
        }

        public async Task<IActionResult> Post(int id, CancellationToken cancellationToken)
        {
            var model = await _receivingReportRepo.FindRR(id, cancellationToken);

            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (model.ReceivedDate == null)
                {
                    TempData["error"] = "Please indicate the received date.";
                    return RedirectToAction(nameof(Index));
                }

                if (!model.IsPosted)
                {
                    model.IsPosted = true;
                    model.PostedBy = _userManager.GetUserName(this.User);
                    model.PostedDate = DateTime.Now;

                    await _receivingReportRepo.PostAsync(model, User, cancellationToken);

                    #region --Audit Trail Recording

                    if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.PostedBy!, $"Posted rr# {model.ReceivingReportNo}", "Receiving Report", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Receiving Report has been Posted.";
                    return RedirectToAction(nameof(Print), new { id });
                }
                else
                {
                    return RedirectToAction(nameof(Print), new { id });
                }
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["error"] = ex.Message;
                return RedirectToAction(nameof(Print), new { id });
            }
        }

        public async Task<IActionResult> Void(int id, CancellationToken cancellationToken)
        {
            var model = await _dbContext.ReceivingReports
                .FirstOrDefaultAsync(x => x.ReceivingReportId == id, cancellationToken);

            var existingInventory = await _dbContext.Inventories
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.Reference == model!.ReceivingReportNo, cancellationToken: cancellationToken);

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

                        await _generalRepo.RemoveRecords<PurchaseJournalBook>(pb => pb.DocumentNo == model.ReceivingReportNo, cancellationToken);
                        await _generalRepo.RemoveRecords<GeneralLedgerBook>(gl => gl.Reference == model.ReceivingReportNo, cancellationToken);
                        await _inventoryRepo.VoidInventory(existingInventory, cancellationToken);
                        await _receivingReportRepo.RemoveQuantityReceived(model.POId, model.QuantityReceived, cancellationToken);
                        model.QuantityReceived = 0;

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.VoidedBy!, $"Voided rr# {model.ReceivingReportNo}", "Receiving Report", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Receiving Report has been Voided.";
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
            var model = await _dbContext.ReceivingReports.FirstOrDefaultAsync(x => x.ReceivingReportId == id, cancellationToken);

            if (model != null)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (!model.IsCanceled)
                    {
                        model.IsCanceled = true;
                        model.CanceledBy = _userManager.GetUserName(this.User);
                        model.CanceledDate = DateTime.Now;
                        model.CanceledQuantity = model.QuantityDelivered < model.QuantityReceived ? model.QuantityDelivered : model.QuantityReceived;
                        model.QuantityDelivered = 0;
                        model.QuantityReceived = 0;
                        model.CancellationRemarks = cancellationRemarks;

                        #region --Audit Trail Recording

                        if (model.OriginalSeriesNumber.IsNullOrEmpty() && model.OriginalDocumentId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(model.CanceledBy!, $"Cancelled rr# {model.ReceivingReportNo}", "Receiving Report", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Receiving Report has been Cancelled.";
                    }
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }

            return NotFound();
        }

        [HttpGet]
        public async Task<IActionResult> GetLiquidations(int id, CancellationToken cancellationToken)
        {
            var po = await _receivingReportRepo.GetPurchaseOrderAsync(id, cancellationToken);

            var rrPostedOnly = await _dbContext
                .ReceivingReports
                .Where(rr => rr.PONo == po.PurchaseOrderNo && rr.IsPosted)
                .ToListAsync(cancellationToken);

            var rr = await _dbContext
                .ReceivingReports
                .Where(rr => rr.PONo == po.PurchaseOrderNo)
                .ToListAsync(cancellationToken);

            var rrNotPosted = await _dbContext
                .ReceivingReports
                .Where(x => x.PONo == po.PurchaseOrderNo && !x.IsPosted && !x.IsCanceled)
                .ToListAsync(cancellationToken);

            var rrCanceled = await _dbContext
                .ReceivingReports
                .Where(x => x.PONo == po.PurchaseOrderNo && x.IsCanceled)
                .ToListAsync(cancellationToken);

            if (po.PurchaseOrderId != 0)
            {
                return Json(new
                {
                    poNo = po.PurchaseOrderNo,
                    poQuantity = po.Quantity.ToString("N2"),
                    rrList = rr,
                    rrListPostedOnly = rrPostedOnly,
                    rrListNotPosted = rrNotPosted,
                    rrListCanceled = rrCanceled
                });
            }

            return Json(null);
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
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "ReceivingReport");

                    var worksheet2 = package.Workbook.Worksheets.FirstOrDefault(ws => ws.Name == "PurchaseOrder");

                    if (worksheet == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
                    }
                    if (worksheet.ToString() != nameof(DynamicView.ReceivingReport))
                    {
                        TempData["error"] = "The Excel file is not related to receiving report.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
                    }

                    #region -- Purchase Order Import --

                    var poRowCount = worksheet2?.Dimension?.Rows ?? 0;
                    var poDictionary = new Dictionary<string, bool>();
                    var purchaseOrderList = await _dbContext
                        .PurchaseOrders
                        .ToListAsync(cancellationToken);


                    for (int row = 2; row <= poRowCount; row++)  // Assuming the first row is the header
                    {
                        if (worksheet2 == null || poRowCount == 0)
                        {
                            continue;
                        }
                        var purchaseOrder = new PurchaseOrder
                        {
                            PurchaseOrderNo = worksheet2.Cells[row, 16].Text,
                            Date = DateOnly.TryParse(worksheet2.Cells[row, 1].Text, out DateOnly dueDate) ? dueDate : default,
                            Terms = worksheet2.Cells[row, 2].Text,
                            Quantity = decimal.TryParse(worksheet2.Cells[row, 3].Text, out decimal quantity) ? quantity : 0,
                            Price = decimal.TryParse(worksheet2.Cells[row, 4].Text, out decimal price) ? price : 0,
                            Amount = decimal.TryParse(worksheet2.Cells[row, 5].Text, out decimal amount) ? amount : 0,
                            FinalPrice = decimal.TryParse(worksheet2.Cells[row, 6].Text, out decimal finalPrice) ? finalPrice : 0,
                            // QuantityReceived = decimal.TryParse(worksheet.Cells[row, 7].Text, out decimal quantityReceived) ? quantityReceived : 0,
                            // IsReceived = bool.TryParse(worksheet.Cells[row, 8].Text, out bool isReceived) ? isReceived : default,
                            // ReceivedDate = DateTime.TryParse(worksheet.Cells[row, 9].Text, out DateTime receivedDate) ? receivedDate : default,
                            Remarks = worksheet2.Cells[row, 10].Text,
                            CreatedBy = worksheet2.Cells[row, 11].Text,
                            CreatedDate = DateTime.TryParse(worksheet2.Cells[row, 12].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet2.Cells[row, 19].Text,
                            PostedDate = DateTime.TryParse(worksheet2.Cells[row, 20].Text, out DateTime postedDate) ? postedDate : default,
                            IsClosed = bool.TryParse(worksheet2.Cells[row, 13].Text, out bool isClosed) && isClosed,
                            CancellationRemarks = worksheet2.Cells[row, 14].Text != "" ? worksheet2.Cells[row, 14].Text : null,
                            OriginalProductId = int.TryParse(worksheet2.Cells[row, 15].Text, out int originalProductId) ? originalProductId : 0,
                            OriginalSeriesNumber = worksheet2.Cells[row, 16].Text,
                            OriginalSupplierId = int.TryParse(worksheet2.Cells[row, 17].Text, out int originalSupplierId) ? originalSupplierId : 0,
                            OriginalDocumentId = int.TryParse(worksheet2.Cells[row, 18].Text, out int originalDocumentId) ? originalDocumentId : 0,
                        };

                        if (!poDictionary.TryAdd(purchaseOrder.OriginalSeriesNumber, true))
                        {
                            continue;
                        }

                        if (purchaseOrderList.Any(po => po.OriginalDocumentId == purchaseOrder.OriginalDocumentId))
                        {
                            var poChanges = new Dictionary<string, (string OriginalValue, string NewValue)>();
                            var existingPo = await _dbContext.PurchaseOrders.FirstOrDefaultAsync(si => si.OriginalDocumentId == purchaseOrder.OriginalDocumentId, cancellationToken);

                            if (existingPo!.PurchaseOrderNo!.TrimStart().TrimEnd() != worksheet2.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                poChanges["PONo"] = (existingPo.PurchaseOrderNo.TrimStart().TrimEnd(), worksheet2.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet2.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Date"] = (existingPo.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet2.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.Terms.TrimStart().TrimEnd() != worksheet2.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Terms"] = (existingPo.Terms.TrimStart().TrimEnd(), worksheet2.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.Quantity.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Quantity"] = (existingPo.Quantity.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[row, 3].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPo.Price.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Price"] = (existingPo.Price.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[row, 4].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPo.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["Amount"] = (existingPo.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[row, 5].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingPo.FinalPrice?.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet2.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                poChanges["FinalPrice"] = (existingPo.FinalPrice?.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet2.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())!;
                            }

                            if (existingPo.Remarks.TrimStart().TrimEnd() != worksheet2.Cells[row, 10].Text.TrimStart().TrimEnd())
                            {
                                poChanges["Remarks"] = (existingPo.Remarks.TrimStart().TrimEnd(), worksheet2.Cells[row, 10].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.CreatedBy!.TrimStart().TrimEnd() != worksheet2.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CreatedBy"] = (existingPo.CreatedBy.TrimStart().TrimEnd(), worksheet2.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet2.Cells[row, 12].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CreatedDate"] = (existingPo.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet2.Cells[row, 12].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.IsClosed.ToString().ToUpper().TrimStart().TrimEnd() != worksheet2.Cells[row, 13].Text.TrimStart().TrimEnd())
                            {
                                poChanges["IsClosed"] = (existingPo.IsClosed.ToString().ToUpper().TrimStart().TrimEnd(), worksheet2.Cells[row, 13].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingPo.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingPo.CancellationRemarks.TrimStart().TrimEnd()) != worksheet2.Cells[row, 14].Text.TrimStart().TrimEnd())
                            {
                                poChanges["CancellationRemarks"] = (existingPo.CancellationRemarks?.TrimStart().TrimEnd(), worksheet2.Cells[row, 14].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingPo.OriginalProductId.ToString()!.TrimStart().TrimEnd() != (worksheet2.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet2.Cells[row, 15].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["OriginalProductId"] = (existingPo.OriginalProductId.ToString()!.TrimStart().TrimEnd(), worksheet2.Cells[row, 15].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet2.Cells[row, 15].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.OriginalSeriesNumber!.TrimStart().TrimEnd() != worksheet2.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                poChanges["OriginalSeriesNumber"] = (existingPo.OriginalSeriesNumber.TrimStart().TrimEnd(), worksheet2.Cells[row, 16].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.OriginalSupplierId.ToString()!.TrimStart().TrimEnd() != (worksheet2.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet2.Cells[row, 17].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["SupplierId"] = (existingPo.SupplierId.ToString()!.TrimStart().TrimEnd(), worksheet2.Cells[row, 17].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet2.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if (existingPo.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet2.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet2.Cells[row, 18].Text.TrimStart().TrimEnd()))
                            {
                                poChanges["OriginalDocumentId"] = (existingPo.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet2.Cells[row, 18].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 18].Text.TrimStart().TrimEnd());
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

                    var rowCount = worksheet.Dimension.Rows;
                    var rrDictionary = new Dictionary<string, bool>();
                    var receivingReportList = await _dbContext
                        .ReceivingReports
                        .ToListAsync(cancellationToken);
                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        var receivingReport = new ReceivingReport
                        {
                            ReceivingReportNo = worksheet.Cells[row, 21].Text,
                            Date = DateOnly.TryParse(worksheet.Cells[row, 1].Text, out DateOnly date) ? date : default,
                            DueDate = DateOnly.TryParse(worksheet.Cells[row, 2].Text, out DateOnly dueDate) ? dueDate : default,
                            SupplierInvoiceNumber = worksheet.Cells[row, 3].Text != "" ? worksheet.Cells[row, 3].Text : null,
                            SupplierInvoiceDate = worksheet.Cells[row, 4].Text,
                            TruckOrVessels = worksheet.Cells[row, 5].Text,
                            QuantityDelivered = decimal.TryParse(worksheet.Cells[row, 6].Text, out decimal quantityDelivered) ? quantityDelivered : 0,
                            QuantityReceived = decimal.TryParse(worksheet.Cells[row, 7].Text, out decimal quantityReceived) ? quantityReceived : 0,
                            GainOrLoss = decimal.TryParse(worksheet.Cells[row, 8].Text, out decimal gainOrLoss) ? gainOrLoss : 0,
                            Amount = decimal.TryParse(worksheet.Cells[row, 9].Text, out decimal amount) ? amount : 0,
                            OtherRef = worksheet.Cells[row, 10].Text != "" ? worksheet.Cells[row, 10].Text : null,
                            Remarks = worksheet.Cells[row, 11].Text,
                            // AmountPaid = decimal.TryParse(worksheet.Cells[row, 12].Text, out decimal amountPaid) ? amountPaid : 0,
                            // IsPaid = bool.TryParse(worksheet.Cells[row, 13].Text, out bool IsPaid) ? IsPaid : default,
                            // PaidDate = DateTime.TryParse(worksheet.Cells[row, 14].Text, out DateTime paidDate) ? paidDate : DateTime.MinValue,
                            // CanceledQuantity = decimal.TryParse(worksheet.Cells[row, 15].Text, out decimal netAmountOfEWT) ? netAmountOfEWT : 0,
                            CreatedBy = worksheet.Cells[row, 16].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 17].Text, out DateTime createdDate) ? createdDate : default,
                            PostedBy = worksheet.Cells[row, 23].Text,
                            PostedDate = DateTime.TryParse(worksheet.Cells[row, 24].Text, out DateTime postedDate) ? postedDate : default,
                            CancellationRemarks = worksheet.Cells[row, 18].Text != "" ? worksheet.Cells[row, 18].Text : null,
                            ReceivedDate = DateOnly.TryParse(worksheet.Cells[row, 19].Text, out DateOnly receivedDate) ? receivedDate : default,
                            OriginalPOId = int.TryParse(worksheet.Cells[row, 20].Text, out int originalPoId) ? originalPoId : 0,
                            OriginalSeriesNumber = worksheet.Cells[row, 21].Text,
                            OriginalDocumentId = int.TryParse(worksheet.Cells[row, 22].Text, out int originalDocumentId) ? originalDocumentId : 0,
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

                            if (existingRr!.ReceivingReportNo!.TrimStart().TrimEnd() != worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["RRNo"] = (existingRr.ReceivingReportNo.TrimStart().TrimEnd(), worksheet.Cells[row, 21].Text.TrimStart().TrimEnd());
                            }

                            if (existingRr.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 1].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["Date"] = (existingRr.Date.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 1].Text.TrimStart().TrimEnd());
                            }

                            if (existingRr.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != worksheet.Cells[row, 2].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["DueDate"] = (existingRr.DueDate.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 2].Text.TrimStart().TrimEnd());
                            }

                            if (existingRr.SupplierInvoiceNumber?.TrimStart().TrimEnd() != (worksheet.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet.Cells[row, 3].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["SupplierInvoiceNumber"] = (existingRr.SupplierInvoiceNumber?.TrimStart().TrimEnd(), worksheet.Cells[row, 3].Text.TrimStart().TrimEnd() == "" ? null : worksheet.Cells[row, 3].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.SupplierInvoiceDate?.TrimStart().TrimEnd() != worksheet.Cells[row, 4].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["SupplierInvoiceDate"] = (existingRr.SupplierInvoiceDate?.TrimStart().TrimEnd(), worksheet.Cells[row, 4].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.TruckOrVessels.TrimStart().TrimEnd() != worksheet.Cells[row, 5].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["TruckOrVessels"] = (existingRr.TruckOrVessels.TrimStart().TrimEnd(), worksheet.Cells[row, 5].Text.TrimStart().TrimEnd());
                            }

                            if (existingRr.QuantityDelivered.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["QuantityDelivered"] = (existingRr.QuantityDelivered.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 6].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingRr.QuantityReceived.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["QuantityReceived"] = (existingRr.QuantityReceived.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 7].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingRr.GainOrLoss.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["GainOrLoss"] = (existingRr.GainOrLoss.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 8].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingRr.Amount.ToString("F2").TrimStart().TrimEnd() != decimal.Parse(worksheet.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd())
                            {
                                rrChanges["Amount"] = (existingRr.Amount.ToString("F2").TrimStart().TrimEnd(), decimal.Parse(worksheet.Cells[row, 9].Text).ToString("F2").TrimStart().TrimEnd());
                            }

                            if (existingRr.OtherRef?.TrimStart().TrimEnd() != (worksheet.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet.Cells[row, 10].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OtherRef"] = (existingRr.OtherRef?.TrimStart().TrimEnd(), worksheet.Cells[row, 10].Text.TrimStart().TrimEnd() == "" ? null : worksheet.Cells[row, 10].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.Remarks.TrimStart().TrimEnd() != worksheet.Cells[row, 11].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["Remarks"] = (existingRr.Remarks.TrimStart().TrimEnd(), worksheet.Cells[row, 11].Text.TrimStart().TrimEnd());
                            }

                            if (existingRr.CreatedBy?.TrimStart().TrimEnd() != worksheet.Cells[row, 16].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CreatedBy"] = (existingRr.CreatedBy?.TrimStart().TrimEnd(), worksheet.Cells[row, 16].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd() != worksheet.Cells[row, 17].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CreatedDate"] = (existingRr.CreatedDate.ToString("yyyy-MM-dd hh:mm:ss.ffffff").TrimStart().TrimEnd(), worksheet.Cells[row, 17].Text.TrimStart().TrimEnd());
                            }

                            if ((string.IsNullOrWhiteSpace(existingRr.CancellationRemarks?.TrimStart().TrimEnd()) ? "" : existingRr.CancellationRemarks.TrimStart().TrimEnd()) != worksheet.Cells[row, 18].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["CancellationRemarks"] = (existingRr.CancellationRemarks?.TrimStart().TrimEnd(), worksheet.Cells[row, 18].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd() != (worksheet.Cells[row, 19].Text.TrimStart().TrimEnd() == "" ? DateOnly.MinValue.ToString("yyyy-MM-dd").TrimStart().TrimEnd() : worksheet.Cells[row, 19].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["ReceivedDate"] = (existingRr.ReceivedDate?.ToString("yyyy-MM-dd").TrimStart().TrimEnd(), worksheet.Cells[row, 19].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.OriginalPOId?.ToString().TrimStart().TrimEnd() != (worksheet.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 20].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OriginalPOId"] = (existingRr.OriginalPOId?.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 20].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 20].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.OriginalSeriesNumber?.TrimStart().TrimEnd() != worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())
                            {
                                rrChanges["OriginalSeriesNumber"] = (existingRr.OriginalSeriesNumber?.TrimStart().TrimEnd(), worksheet.Cells[row, 21].Text.TrimStart().TrimEnd())!;
                            }

                            if (existingRr.OriginalDocumentId.ToString().TrimStart().TrimEnd() != (worksheet.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 22].Text.TrimStart().TrimEnd()))
                            {
                                rrChanges["OriginalDocumentId"] = (existingRr.OriginalDocumentId.ToString().TrimStart().TrimEnd(), worksheet.Cells[row, 22].Text.TrimStart().TrimEnd() == "" ? 0.ToString() : worksheet.Cells[row, 22].Text.TrimStart().TrimEnd());
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
                                AuditTrail auditTrailBook = new(receivingReport.PostedBy, $"Posted receiving report# {receivingReport.ReceivingReportNo}", "Receiving report", ipAddress!, receivingReport.PostedDate);
                                await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                            }

                            #endregion --Audit Trail Recording
                        }

                        var getPo = await _dbContext
                            .PurchaseOrders
                            .Where(po => po.OriginalDocumentId == receivingReport.OriginalPOId)
                            .FirstOrDefaultAsync(cancellationToken);

                        receivingReport.POId = getPo?.PurchaseOrderId;
                        receivingReport.PONo = getPo?.PurchaseOrderNo;

                        await _dbContext.ReceivingReports.AddAsync(receivingReport, cancellationToken);
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    var checkChangesOfRecord = await _dbContext.ImportExportLogs
                        .Where(iel => iel.Action == string.Empty).ToListAsync(cancellationToken);
                    if (checkChangesOfRecord.Any())
                    {
                        TempData["importChanges"] = "";
                    }

                    #endregion -- Receiving Report Import --
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
                }
                catch (InvalidOperationException ioe)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["warning"] = ioe.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.ReceivingReport });
        }

        #endregion -- import xlsx record --
    }
}
