using System.Diagnostics.CodeAnalysis;
using Accounting_System.Data;
using Accounting_System.Models.MasterFile;
using Accounting_System.Models.Reports;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class SupplierController : Controller
    {
        private readonly ApplicationDbContext _context;

        private readonly SupplierRepo _supplierRepo;

        private readonly IWebHostEnvironment _webHostEnvironment;

        public SupplierController(ApplicationDbContext context, SupplierRepo supplierRepo, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _supplierRepo = supplierRepo;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var data = await _context.Suppliers.ToListAsync(cancellationToken);

            if (view == nameof(DynamicView.Supplier))
            {
                return View("ImportExportIndex", data);
            }

            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllSupplierIds(CancellationToken cancellationToken)
        {
            var supplierIds = await _context.Suppliers
                                     .Select(s => s.SupplierId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(supplierIds);
        }


        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            Supplier model = new()
            {
                DefaultExpenses = await _context.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber + " " + s.AccountName,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken),
                WithholdingTaxList = await _context.ChartOfAccounts
                    .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountNumber + " " + s.AccountName,
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Supplier supplier, IFormFile? document, IFormFile? registration, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (await _supplierRepo.IsSupplierNameExist(supplier.SupplierName, supplier.Category, cancellationToken))
                    {
                        ModelState.AddModelError("Name", "Supplier name already exist!");
                        supplier.DefaultExpenses = await _context.ChartOfAccounts
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        supplier.WithholdingTaxList = await _context.ChartOfAccounts
                            .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        return View(supplier);
                    }

                    if (await _supplierRepo.IsSupplierTinExist(supplier.SupplierTin, supplier.Category, cancellationToken))
                    {
                        ModelState.AddModelError("TinNo", "Supplier tin already exist!");
                        supplier.DefaultExpenses = await _context.ChartOfAccounts
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        supplier.WithholdingTaxList = await _context.ChartOfAccounts
                            .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        return View(supplier);
                    }

                    supplier.CreatedBy = User.Identity!.Name;
                    supplier.Number = await _supplierRepo.GetLastNumber(cancellationToken);
                    if (supplier.WithholdingTaxtitle != null && supplier.WithholdingTaxPercent != 0)
                    {
                        supplier.WithholdingTaxPercent = supplier.WithholdingTaxtitle.StartsWith("2010302") ? 1 : 2;
                    }

                    if (document != null && document.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Proof of Exemption", supplier.Number.ToString());

                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        string fileName = Path.GetFileName(document.FileName);
                        string fileSavePath = Path.Combine(uploadsFolder, fileName);

                        await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                        {
                            await document.CopyToAsync(stream, cancellationToken);
                        }

                        supplier.ProofOfExemptionFilePath = fileSavePath;
                    }

                    if (registration != null && registration.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Proof of Registration", supplier.Number.ToString());

                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        string fileName = Path.GetFileName(registration.FileName);
                        string fileSavePath = Path.Combine(uploadsFolder, fileName);

                        await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                        {
                            await registration.CopyToAsync(stream, cancellationToken);
                        }

                        supplier.ProofOfRegistrationFilePath = fileSavePath;
                    }
                    else
                    {
                        TempData["error"] = "There's something wrong in your file. Contact MIS.";
                        supplier.DefaultExpenses = await _context.ChartOfAccounts
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        supplier.WithholdingTaxList = await _context.ChartOfAccounts
                            .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                            .Select(s => new SelectListItem
                            {
                                Value = s.AccountNumber + " " + s.AccountName,
                                Text = s.AccountNumber + " " + s.AccountName
                            })
                            .ToListAsync(cancellationToken);
                        return View(supplier);
                    }

                    await _context.AddAsync(supplier, cancellationToken);

                    #region --Audit Trail Recording

                    if (supplier.OriginalSupplierId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(supplier.CreatedBy!, $"Create new supplier {supplier.SupplierName}", "Supplier Master File", ipAddress!);
                        await _context.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = $"Supplier {supplier.SupplierName} has been created.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(Index));
                }
            }
            return View(supplier);
        }

        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || !_context.Suppliers.Any())
            {
                return NotFound();
            }

            var supplier = await _context.Suppliers.FirstOrDefaultAsync(x => x.SupplierId == id, cancellationToken);
            if (supplier == null)
            {
                return NotFound();
            }
            supplier.DefaultExpenses = await _context.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber + " " + s.AccountName,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);
            supplier.WithholdingTaxList = await _context.ChartOfAccounts
                .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                .Select(s => new SelectListItem
                {
                    Value = s.AccountNumber + " " + s.AccountName,
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);
            return View(supplier);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Supplier supplier, IFormFile? document, IFormFile? registration, CancellationToken cancellationToken)
        {
            if (id != supplier.SupplierId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var existingModel = await _context.Suppliers.FirstOrDefaultAsync(x => x.SupplierId == supplier.SupplierId, cancellationToken);

                    existingModel!.ReasonOfExemption = supplier.ReasonOfExemption;
                    existingModel.Validity = supplier.Validity;
                    existingModel.ValidityDate = supplier.ValidityDate;
                    existingModel.SupplierName = supplier.SupplierName;
                    existingModel.SupplierAddress = supplier.SupplierAddress;
                    existingModel.SupplierTin = supplier.SupplierTin;
                    existingModel.SupplierTerms = supplier.SupplierTerms;
                    existingModel.VatType = supplier.VatType;
                    existingModel.TaxType = supplier.TaxType;
                    existingModel.Category = supplier.Category;
                    existingModel.TradeName = supplier.TradeName;
                    existingModel.Branch = supplier.Branch;
                    existingModel.ZipCode = supplier.ZipCode;
                    existingModel.DefaultExpenseNumber = supplier.DefaultExpenseNumber;
                    if (existingModel.WithholdingTaxtitle != null && existingModel.WithholdingTaxPercent != 0)
                    {
                        existingModel.WithholdingTaxPercent = supplier.WithholdingTaxtitle!.StartsWith("2010302") ? 1 : 2;
                    }
                    existingModel.WithholdingTaxtitle = supplier.WithholdingTaxtitle;
                    supplier.Number = existingModel.Number;

                    #region -- Upload file --

                    if (document != null && document.Length > 0)
                    {
                        string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "Proof of Exemption", supplier.Number.ToString());

                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        string fileName = Path.GetFileName(document.FileName);
                        string fileSavePath = Path.Combine(uploadsFolder, fileName);

                        await using (FileStream stream = new FileStream(fileSavePath, FileMode.Create))
                        {
                            await document.CopyToAsync(stream, cancellationToken);
                        }

                        existingModel.ProofOfExemptionFilePath = fileSavePath;
                    }

                    #endregion -- Upload file --

                    if (_context.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (supplier.OriginalSupplierId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!, $"Update supplier {supplier.SupplierName}", "Supplier Master File", ipAddress!);
                            await _context.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _context.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = $"Supplier {supplier.SupplierName} has been edited.";
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (!SupplierExists(supplier.SupplierId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    supplier.DefaultExpenses = await _context.ChartOfAccounts
                        .Select(s => new SelectListItem
                        {
                            Value = s.AccountNumber + " " + s.AccountName,
                            Text = s.AccountNumber + " " + s.AccountName
                        })
                        .ToListAsync(cancellationToken);
                    supplier.WithholdingTaxList = await _context.ChartOfAccounts
                        .Where(coa => coa.AccountNumber == "201030210" || coa.AccountNumber == "201030220")
                        .Select(s => new SelectListItem
                        {
                            Value = s.AccountNumber + " " + s.AccountName,
                            Text = s.AccountNumber + " " + s.AccountName
                        })
                        .ToListAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(supplier);
                }
                return RedirectToAction(nameof(Index));
            }
            return View(supplier);
        }

        private bool SupplierExists(int id)
        {
            return _context.Suppliers != null! && _context.Suppliers.Any(e => e.SupplierId == id);
        }

        //Upload as .xlsx file.(Import)

        #region -- import xlsx record --

        [HttpPost]
        [SuppressMessage("ReSharper.DPA", "DPA0000: DPA issues")]
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
                await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

                try
                {
                    using var package = new ExcelPackage(stream);
                    var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (worksheet == null)
                    {
                        TempData["error"] = "The Excel file contains no worksheets.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.Supplier });
                    }
                    if (worksheet.ToString() != nameof(DynamicView.Supplier))
                    {
                        TempData["error"] = "The Excel file is not related to supplier master file.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.Supplier });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var supplierList = await _context
                        .Suppliers
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        var supplier = new Supplier
                        {
                            Number = await _supplierRepo.GetLastNumber(cancellationToken),
                            SupplierName = worksheet.Cells[row, 1].Text,
                            SupplierAddress = worksheet.Cells[row, 2].Text,
                            ZipCode = worksheet.Cells[row, 3].Text,
                            SupplierTin = worksheet.Cells[row, 4].Text,
                            SupplierTerms = worksheet.Cells[row, 5].Text,
                            VatType = worksheet.Cells[row, 6].Text,
                            TaxType = worksheet.Cells[row, 7].Text,
                            ProofOfRegistrationFilePath = worksheet.Cells[row, 8].Text,
                            ReasonOfExemption = worksheet.Cells[row, 9].Text,
                            Validity = worksheet.Cells[row, 10].Text,
                            ValidityDate = DateTime.TryParse(worksheet.Cells[row, 11].Text, out DateTime validityDate) ? validityDate : default,
                            ProofOfExemptionFilePath = worksheet.Cells[row, 12].Text,
                            CreatedBy = worksheet.Cells[row, 13].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 14].Text, out DateTime createdDate) ? createdDate : default,
                            Branch = worksheet.Cells[row, 15].Text,
                            Category = worksheet.Cells[row, 16].Text,
                            TradeName = worksheet.Cells[row, 17].Text,
                            DefaultExpenseNumber = worksheet.Cells[row, 18].Text,
                            WithholdingTaxPercent = int.TryParse(worksheet.Cells[row, 19].Text, out int withholdingTaxPercent) ? withholdingTaxPercent : 0,
                            WithholdingTaxtitle = worksheet.Cells[row, 20].Text,
                            OriginalSupplierId = int.TryParse(worksheet.Cells[row, 21].Text, out int originalSupplierId) ? originalSupplierId : 0,
                        };

                        if (supplierList.Any(supp => supp.OriginalSupplierId == supplier.OriginalSupplierId))
                        {
                            continue;
                        }

                        await _context.Suppliers.AddAsync(supplier, cancellationToken);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Supplier });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Supplier });
                }
            }

            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.Supplier });
        }

        #endregion -- import xlsx record --
    }
}
