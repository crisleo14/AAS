using System.Globalization;
using Accounting_System.Data;
using Accounting_System.Models.MasterFile;
using Accounting_System.Models.Reports;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class ProductController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly ProductRepository _productRepository;

        public ProductController(ApplicationDbContext dbContext, ProductRepository productRepository)
        {
            _dbContext = dbContext;
            _productRepository = productRepository;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var data = await _dbContext.Products.ToListAsync(cancellationToken);

            if (view == nameof(DynamicView.Product))
            {
                return View("ImportExportIndex", data);
            }

            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllProductIds(CancellationToken cancellationToken)
        {
            var productIds = await _dbContext.Products
                                     .Select(p => p.ProductId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(productIds);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Product product, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (await _productRepository.IsProductCodeExist(product.ProductCode!, cancellationToken))
                    {
                        ModelState.AddModelError("Code", "Product code already exist!");
                        return View(product);
                    }

                    if (await _productRepository.IsProductNameExist(product.ProductName, cancellationToken))
                    {
                        ModelState.AddModelError("Name", "Product name already exist!");
                        return View(product);
                    }

                    product.CreatedBy = User.Identity!.Name!.ToUpper();

                    #region --Audit Trail Recording

                    if (product.OriginalProductId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(product.CreatedBy, $"Created new product {product.ProductName}", "Product", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.AddAsync(product, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Product created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }
            return View(product);
        }

        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || !_dbContext.Products.Any())
            {
                return NotFound();
            }

            var product = await _dbContext.Products.FirstOrDefaultAsync(x => x.ProductId == id, cancellationToken);
            if (product == null)
            {
                return NotFound();
            }
            return View(product);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Product product, CancellationToken cancellationToken)
        {
            if (id != product.ProductId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    var existingProduct = await _dbContext.Products.FirstOrDefaultAsync(x => x.ProductId == product.ProductId, cancellationToken);
                    existingProduct!.ProductCode = product.ProductCode;
                    existingProduct.ProductName = product.ProductName;
                    existingProduct.ProductUnit = product.ProductUnit;
                    existingProduct.ProductId = product.ProductId;
                    existingProduct.CreatedBy = product.CreatedBy;
                    existingProduct.CreatedDate = product.CreatedDate;

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (product.OriginalProductId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!, $"Updated product {product.ProductName}", "Product", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Product updated successfully";
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }

                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (!ProductExists(product.ProductId))
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
                    TempData["error"] = ex.Message;
                    return View(product);
                }
                return RedirectToAction(nameof(Index));
            }
            return View(product);
        }

        private bool ProductExists(int id)
        {
            return _dbContext.Products != null! && _dbContext.Products.Any(e => e.ProductId == id);
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
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet == null)
                        {
                            TempData["error"] = "The Excel file contains no worksheets.";
                            return RedirectToAction(nameof(Index), new { view = DynamicView.Product });
                        }
                        if (worksheet.ToString() != nameof(DynamicView.Product))
                        {
                            TempData["error"] = "The Excel file is not related to product master file.";
                            return RedirectToAction(nameof(Index), new { view = DynamicView.Product });
                        }

                        var rowCount = worksheet.Dimension.Rows;
                        var productList = await _dbContext
                            .Products
                            .ToListAsync(cancellationToken);

                        for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                        {
                            var product = new Product
                            {
                                ProductCode = worksheet.Cells[row, 1].Text,
                                ProductName = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(worksheet.Cells[row, 2].Text.ToLower()),
                                ProductUnit = worksheet.Cells[row, 3].Text,
                                CreatedBy = worksheet.Cells[row, 4].Text,
                                CreatedDate = DateTime.TryParse(worksheet.Cells[row, 5].Text, out DateTime createdDate) ? createdDate : default,
                                OriginalProductId = int.TryParse(worksheet.Cells[row, 6].Text, out int originalProductId) ? originalProductId : 0,
                            };

                            if (productList.Any(p => p.OriginalProductId == product.OriginalProductId))
                            {
                                continue;
                            }

                            await _dbContext.Products.AddAsync(product, cancellationToken);
                        }
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
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
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Product });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Product });
                }
            }

            return RedirectToAction(nameof(Index), new { view = DynamicView.Product });
        }

        #endregion -- import xlsx record --
    }
}
