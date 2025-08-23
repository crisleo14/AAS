using Accounting_System.Data;
using Accounting_System.Models;
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
    public class ServiceController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly ServiceRepo _serviceRepo;

        public ServiceController(ApplicationDbContext dbContext, ServiceRepo serviceRepo)
        {
            _dbContext = dbContext;
            _serviceRepo = serviceRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var data = await _dbContext.Services.ToListAsync(cancellationToken);

            if (view == nameof(DynamicView.Service))
            {
                return View("ImportExportIndex", data);
            }

            return View(data);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllServiceIds(CancellationToken cancellationToken)
        {
            var serviceIds = await _dbContext.Services
                                     .Select(s => s.ServiceId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(serviceIds);
        }

        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var viewModel = new Services
            {
                CurrentAndPreviousTitles = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountId.ToString(),
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken),
                UnearnedTitles = await _dbContext.ChartOfAccounts
                    .Where(coa => !coa.HasChildren)
                    .OrderBy(coa => coa.AccountId)
                    .Select(s => new SelectListItem
                    {
                        Value = s.AccountId.ToString(),
                        Text = s.AccountNumber + " " + s.AccountName
                    })
                    .ToListAsync(cancellationToken)
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Services services, CancellationToken cancellationToken)
        {
            services.CurrentAndPreviousTitles = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountId.ToString(),
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            services.UnearnedTitles = await _dbContext.ChartOfAccounts
                .Where(coa => !coa.HasChildren)
                .OrderBy(coa => coa.AccountId)
                .Select(s => new SelectListItem
                {
                    Value = s.AccountId.ToString(),
                    Text = s.AccountNumber + " " + s.AccountName
                })
                .ToListAsync(cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (await _serviceRepo.IsServicesExist(services.Name, cancellationToken))
                    {
                        ModelState.AddModelError("Name", "Services already exist!");
                        return View(services);
                    }

                    if (services.Percent == 0)
                    {
                        ModelState.AddModelError("Percent", "Please input percent!");
                        return View(services);
                    }

                    var currentAndPrevious = await _dbContext.ChartOfAccounts
                        .FirstOrDefaultAsync(x => x.AccountId == services.CurrentAndPreviousId, cancellationToken);

                    var unearned = await _dbContext.ChartOfAccounts
                        .FirstOrDefaultAsync(x => x.AccountId == services.UnearnedId, cancellationToken);

                    services.CurrentAndPreviousNo = currentAndPrevious!.AccountNumber;
                    services.CurrentAndPreviousTitle = currentAndPrevious.AccountName;

                    services.UnearnedNo = unearned!.AccountNumber;
                    services.UnearnedTitle = unearned.AccountName;

                    services.CreatedBy = User.Identity!.Name!.ToUpper();
                    services.ServiceNo = await _serviceRepo.GetLastNumber(cancellationToken);

                    #region --Audit Trail Recording

                    if (services.OriginalServiceId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(services.CreatedBy, $"Created new service {services.Name}", "Service", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.AddAsync(services, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Services created successfully";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(Index));
                }
            }
            return View(services);
        }

        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || !_dbContext.Services.Any())
            {
                return NotFound();
            }

            var services = await _dbContext.Services.FirstOrDefaultAsync(x => x.ServiceId == id, cancellationToken);
            if (services == null)
            {
                return NotFound();
            }
            return View(services);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Services services, CancellationToken cancellationToken)
        {
            if (id != services.ServiceId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                if (services.Percent == 0)
                {
                    ModelState.AddModelError("Percent", "Please input percent!");
                    return View(services);
                }
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {

                    var existingServices = await _dbContext.Services.FirstOrDefaultAsync(x => x.ServiceId == services.ServiceId, cancellationToken);
                    existingServices!.Name = services.Name;
                    existingServices.Percent = services.Percent;

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (services.OriginalServiceId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!, $"Update service {services.Name}", "Service", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Services updated successfully";
                    }
                    else
                    {
                        throw new InvalidOperationException("No data changes!");
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (!ServicesExists(services.ServiceId))
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
                    return View(services);
                }
                return RedirectToAction(nameof(Index));
            }
            return View(services);
        }

        private bool ServicesExists(int id)
        {
            return _dbContext.Services != null! && _dbContext.Services.Any(e => e.ServiceId == id);
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
                        return RedirectToAction(nameof(Index), new { view = DynamicView.Service });
                    }
                    if (worksheet.ToString() != "Services")
                    {
                        TempData["error"] = "The Excel file is not related to service master file.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.Service });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var servicesList = await _dbContext
                        .Services
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        var services = new Services
                        {
                            ServiceNo = await _serviceRepo.GetLastNumber(cancellationToken),
                            CurrentAndPreviousTitle = worksheet.Cells[row, 1].Text,
                            UnearnedTitle = worksheet.Cells[row, 2].Text,
                            Name = worksheet.Cells[row, 3].Text,
                            Percent = int.TryParse(worksheet.Cells[row, 4].Text, out int percent) ? percent : 0,
                            CreatedBy = worksheet.Cells[row, 5].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 6].Text, out DateTime createdDate) ? createdDate : default,
                            CurrentAndPreviousNo = worksheet.Cells[row, 7].Text,
                            UnearnedNo = worksheet.Cells[row, 8].Text,
                            OriginalServiceId = int.TryParse(worksheet.Cells[row, 9].Text, out int originalServiceId) ? originalServiceId : 0,
                        };

                        if (servicesList.Any(s => s.OriginalServiceId == services.OriginalServiceId))
                        {
                            continue;
                        }

                        await _dbContext.Services.AddAsync(services, cancellationToken);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Service });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.Service });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.Service });
        }

        #endregion -- import xlsx record --
    }
}
