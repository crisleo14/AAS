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

        public ChartOfAccountController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, ChartOfAccountRepo coaRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _coaRepo = coaRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var chartOfAccounts = await _coaRepo.GetChartOfAccountAsync(cancellationToken);

            if (view == nameof(DynamicView.ChartOfAccount))
            {
                return View("ImportExportIndex", chartOfAccounts);
            }
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
                try
                {
                    chartOfAccount.CreatedBy = _userManager.GetUserName(this.User);

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
                            existingModel.EditedBy = _userManager.GetUserName(this.User);
                            existingModel.EditedDate = DateTime.Now;

                            #region --Audit Trail Recording

                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!,
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
                        return RedirectToAction(nameof(Index), new { view = DynamicView.ChartOfAccount });
                    }
                    if (worksheet.ToString() != nameof(DynamicView.ChartOfAccount))
                    {
                        TempData["error"] = "The Excel file is not related to chart of account.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.ChartOfAccount });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var chartOfAccountList = await _dbContext
                        .ChartOfAccounts
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++)  // Assuming the first row is the header
                    {
                        ChartOfAccount? getParent = null;
                        if (!worksheet.Cells[row, 12].Text.IsNullOrEmpty())
                        {
                             getParent = await _dbContext.ChartOfAccounts.FirstOrDefaultAsync(x => x.ParentAccountId == int.Parse(worksheet.Cells[row, 12].Text), cancellationToken);
                        }
                        var coa = new ChartOfAccount
                        {
                            IsMain = bool.TryParse(worksheet.Cells[row, 1].Text, out bool isMain) && isMain,
                            AccountNumber = worksheet.Cells[row, 2].Text,
                            AccountName = worksheet.Cells[row, 3].Text,
                            AccountType = worksheet.Cells[row, 4].Text,
                            NormalBalance = worksheet.Cells[row, 5].Text,
                            Level = int.TryParse(worksheet.Cells[row, 6].Text, out int level) ? level : 0,
                            CreatedBy = worksheet.Cells[row, 7].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 8].Text, out DateTime createdDate) ? createdDate : default,
                            EditedBy = worksheet.Cells[row, 9].Text,
                            EditedDate = DateTime.TryParse(worksheet.Cells[row, 10].Text, out DateTime editedDate) ? editedDate : default,
                            HasChildren = bool.TryParse(worksheet.Cells[row, 11].Text, out bool hasChildren) && hasChildren,
                            ParentAccountId = getParent?.AccountId ?? null,
                            OriginalChartOfAccountId = int.TryParse(worksheet.Cells[row, 13].Text, out int originalChartOfAccountId) ? originalChartOfAccountId : 0,
                            Parent = worksheet.Cells[row, 14].Text ?? string.Empty,
                        };

                        if (chartOfAccountList.Any(c => c.OriginalChartOfAccountId == coa.OriginalChartOfAccountId))
                        {
                            continue;
                        }

                        await _dbContext.ChartOfAccounts.AddAsync(coa, cancellationToken);
                        await _dbContext.SaveChangesAsync(cancellationToken);
                    }


                    //refresh data set
                    chartOfAccountList = await _dbContext.ChartOfAccounts.ToListAsync(cancellationToken);

                    var excelRowCount = worksheet.Dimension.Rows;

                    for (int rows = 2; rows <= excelRowCount; rows++)  // Assuming the first row is the header
                    {
                        string cellValue = worksheet.Cells[rows, 12].Text;

                        if (!string.IsNullOrEmpty(cellValue) || int.TryParse(cellValue, out int result) && result != 0)
                        {
                            var existingRecord =
                                chartOfAccountList.FirstOrDefault(c=> c.AccountNumber == worksheet.Cells[rows, 2].Text);
                            var findAccountIdForParentAccountId =
                                chartOfAccountList.FirstOrDefault(c =>
                                    c.OriginalChartOfAccountId == int.Parse(worksheet.Cells[rows, 12].Text));

                            existingRecord!.ParentAccountId = findAccountIdForParentAccountId?.AccountId ?? null;
                        }
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.ChartOfAccount });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.ChartOfAccount });
                }
            }
            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.ChartOfAccount });
        }

        #endregion -- import xlsx record --
    }
}
