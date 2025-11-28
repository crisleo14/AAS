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

        private readonly GeneralRepo _generalRepo;

        public ServiceController(ApplicationDbContext dbContext, ServiceRepo serviceRepo,
            GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _serviceRepo = serviceRepo;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var data = await _dbContext.Services.ToListAsync(cancellationToken);

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
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
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

                    services.CreatedBy = createdBy;
                    services.ServiceNo = await _serviceRepo.GetLastNumber(cancellationToken);

                    #region --Audit Trail Recording

                    if (services.OriginalServiceId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(createdBy, $"Created new service {services.Name}", "Service", ipAddress!);
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
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
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
                            AuditTrail auditTrailBook = new(createdBy, $"Update service {services.Name}", "Service", ipAddress!);
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
    }
}
