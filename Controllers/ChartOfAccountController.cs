using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.Reports;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OfficeOpenXml;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class ChartOfAccountController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly ChartOfAccountRepo _coaRepo;

        private readonly GeneralRepo _generalRepo;

        public ChartOfAccountController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager,
            ChartOfAccountRepo coaRepo, GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _coaRepo = coaRepo;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var chartOfAccounts = await _coaRepo.GetChartOfAccountAsync(cancellationToken);

            return View(chartOfAccounts);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllChartOfAccountIds(CancellationToken cancellationToken)
        {
            var coaIds = await _dbContext.ChartOfAccounts
                                     .Select(coa => coa.AccountId) // Assuming Id is the primary key
                                     .ToListAsync(cancellationToken);
            return Json(coaIds);
        }

        [HttpGet]
        public async Task<IActionResult> Create(CancellationToken cancellationToken)
        {
            var viewModel = new ChartOfAccount
            {
                Main = await _dbContext.ChartOfAccounts
                    .OrderBy(coa => coa.AccountId)
                    .Where(coa => coa.IsMain)
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
        public async Task<IActionResult> Create(ChartOfAccount chartOfAccount, string thirdLevel, string? fourthLevel, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
                try
                {
                    chartOfAccount.CreatedBy = createdBy;

                    if (fourthLevel == "create-new" || fourthLevel == null)
                    {
                        var existingCoa = await _dbContext
                            .ChartOfAccounts
                            .OrderBy(coa => coa.AccountId)
                            .FirstOrDefaultAsync(coa => coa.AccountId == int.Parse(thirdLevel), cancellationToken);

                        if (existingCoa == null)
                        {
                            return NotFound();
                        }

                        chartOfAccount.AccountType = existingCoa.AccountType;
                        chartOfAccount.NormalBalance = existingCoa.NormalBalance;
                        chartOfAccount.Level = existingCoa.Level + 1;
                        chartOfAccount.ParentAccountId = int.Parse(thirdLevel);
                    }
                    else
                    {
                        var existingCoa = await _dbContext
                            .ChartOfAccounts
                            .OrderBy(coa => coa.AccountId)
                            .FirstOrDefaultAsync(coa => coa.AccountId == int.Parse(fourthLevel), cancellationToken);

                        if (existingCoa == null)
                        {
                            return NotFound();
                        }

                        chartOfAccount.AccountType = existingCoa.AccountType;
                        chartOfAccount.NormalBalance = existingCoa.NormalBalance;
                        chartOfAccount.Level = existingCoa.Level + 1;
                        chartOfAccount.ParentAccountId = int.Parse(fourthLevel);
                    }

                    await _dbContext.AddAsync(chartOfAccount, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Chart of account created successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index));
                }
            }
            return View(chartOfAccount);
        }

        public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken)
        {
            if (id == null || !_dbContext.ChartOfAccounts.Any())
            {
                return NotFound();
            }

            var chartOfAccount = await _dbContext.ChartOfAccounts.FirstOrDefaultAsync(x => x.AccountId == id, cancellationToken);
            if (chartOfAccount == null)
            {
                return NotFound();
            }
            return View(chartOfAccount);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ChartOfAccount chartOfAccount, CancellationToken cancellationToken)
        {
            if (id != chartOfAccount.AccountId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
                try
                {
                    var existingModel = await _dbContext.ChartOfAccounts.FirstOrDefaultAsync(x => x.AccountId == id, cancellationToken);
                    if (existingModel != null)
                    {
                        existingModel.IsMain = chartOfAccount.IsMain;
                        existingModel.AccountNumber = chartOfAccount.AccountNumber;
                        existingModel.AccountName = chartOfAccount.AccountName;
                        existingModel.AccountType = chartOfAccount.AccountType;
                        existingModel.NormalBalance = chartOfAccount.NormalBalance;
                        existingModel.Level = chartOfAccount.Level;
                        existingModel.AccountId = chartOfAccount.AccountId;

                        if (_dbContext.ChangeTracker.HasChanges())
                        {
                            existingModel.EditedBy = createdBy;
                            existingModel.EditedDate = DateTime.Now;

                            #region --Audit Trail Recording

                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(createdBy,
                                $"Updated chart of account {chartOfAccount.AccountNumber} {chartOfAccount.AccountName}",
                                "Chart of Account", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);

                            #endregion --Audit Trail Recording

                            await _dbContext.SaveChangesAsync(cancellationToken);
                            await transaction.CommitAsync(cancellationToken);
                            TempData["success"] = "Chart of account updated successfully";
                        }
                        else
                        {
                            throw new InvalidOperationException("No data changes!");
                        }
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    if (!ChartOfAccountExists(chartOfAccount.AccountId))
                    {
                        return NotFound();
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(chartOfAccount);
                }

                return RedirectToAction(nameof(Index));
            }
            return View(chartOfAccount);
        }

        private bool ChartOfAccountExists(int id)
        {
            return id > 0 && _dbContext.ChartOfAccounts.Any(e => e.AccountId == id);
        }

        [HttpGet]
        public async Task<IActionResult> GetChartOfAccount(int number, CancellationToken cancellationToken)
        {
            return Json(await _coaRepo.FindAccountsAsync(number, cancellationToken));
        }

        [HttpGet]
        public async Task<IActionResult> GenerateNumber(int parent, CancellationToken cancellationToken)
        {
            return Json(await _coaRepo.GenerateNumberAsync(parent, cancellationToken));
        }
    }
}
