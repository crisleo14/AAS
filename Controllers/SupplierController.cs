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

        private readonly GeneralRepo _generalRepo;

        public SupplierController(ApplicationDbContext context, SupplierRepo supplierRepo,
            IWebHostEnvironment webHostEnvironment,
            GeneralRepo generalRepo)
        {
            _context = context;
            _supplierRepo = supplierRepo;
            _webHostEnvironment = webHostEnvironment;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var data = await _context.Suppliers.ToListAsync(cancellationToken);

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
    }
}
