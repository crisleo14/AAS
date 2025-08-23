using Accounting_System.Data;
using Accounting_System.Models.MasterFile;
using Accounting_System.Models.Reports;
using Accounting_System.Repository;
using Accounting_System.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace Accounting_System.Controllers
{
    [Authorize]
    public class BankAccountController : Controller
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly UserManager<IdentityUser> _userManager;

        private readonly BankAccountRepo _bankAccountRepo;

        public BankAccountController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager,
            BankAccountRepo bankAccountRepo)
        {
            _dbContext = dbContext;
            this._userManager = userManager;
            _bankAccountRepo = bankAccountRepo;
        }

        public async Task<IActionResult> Index(string? view, CancellationToken cancellationToken)
        {
            var ba = await _bankAccountRepo.GetBankAccountAsync(cancellationToken);

            if (view == nameof(DynamicView.BankAccount))
            {
                return View("ImportExportIndex", ba);
            }

            return View(ba);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllBankAccountIds(CancellationToken cancellationToken)
        {
            var bankAccountIds = await _dbContext.BankAccounts
                .Select(ba => ba.BankAccountId) // Assuming Id is the primary key
                .ToListAsync(cancellationToken);
            return Json(bankAccountIds);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Create(BankAccount model, CancellationToken cancellationToken)
        {
            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (await _bankAccountRepo.IsBankAccountNameExist(model.AccountName, cancellationToken))
                    {
                        ModelState.AddModelError("AccountName", "Bank account name already exist!");
                        return View(model);
                    }

                    model.CreatedBy = _userManager.GetUserName(this.User);

                    #region --Audit Trail Recording

                    if (model.OriginalBankId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(model.CreatedBy!, $"Created new bank {model.AccountName}",
                            "Bank Account", ipAddress!);
                        await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                    }

                    #endregion --Audit Trail Recording

                    await _dbContext.AddAsync(model, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    TempData["success"] = "Bank created successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception ex)
                {
                 await transaction.RollbackAsync(cancellationToken);
                 TempData["error"] = ex.Message;
                 return RedirectToAction(nameof(Index), new { view = DynamicView.ServiceInvoice });
                }
            }
            else
            {
                ModelState.AddModelError("", "The information you submitted is not valid!");
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
        {
            var existingModel = await _bankAccountRepo.FindBankAccount(id, cancellationToken);
            return View(existingModel);
        }

        [HttpPost]
        public async Task<IActionResult> Edit(BankAccount model, CancellationToken cancellationToken)
        {
            var existingModel = await _bankAccountRepo.FindBankAccount(model.BankAccountId, cancellationToken);

            if (ModelState.IsValid)
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    existingModel.AccountName = model.AccountName;
                    existingModel.Bank = model.Bank;

                    if (_dbContext.ChangeTracker.HasChanges())
                    {
                        #region --Audit Trail Recording

                        if (model.OriginalBankId == 0)
                        {
                            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                            AuditTrail auditTrailBook = new(User.Identity!.Name!,
                                $"Updated bank {model.AccountName}", "Bank Account", ipAddress!);
                            await _dbContext.AddAsync(auditTrailBook, cancellationToken);
                        }

                        #endregion --Audit Trail Recording

                        await _dbContext.SaveChangesAsync(cancellationToken);
                        await transaction.CommitAsync(cancellationToken);
                        TempData["success"] = "Bank edited successfully.";
                        return RedirectToAction(nameof(Index));
                    }

                    throw new InvalidOperationException("No data changes!");
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return View(existingModel);
                }
            }
            else
            {
                ModelState.AddModelError("", "The information you submitted is not valid!");
                return View(existingModel);
            }
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
                        return RedirectToAction(nameof(Index), new { view = DynamicView.BankAccount });
                    }

                    if (worksheet.ToString() != nameof(DynamicView.BankAccount))
                    {
                        TempData["error"] = "The Excel file is not related to bank account master file.";
                        return RedirectToAction(nameof(Index), new { view = DynamicView.BankAccount });
                    }

                    var rowCount = worksheet.Dimension.Rows;
                    var bankAccountList = await _dbContext
                        .BankAccounts
                        .ToListAsync(cancellationToken);

                    for (int row = 2; row <= rowCount; row++) // Assuming the first row is the header
                    {
                        var bankAccount = new BankAccount
                        {
                            Bank = worksheet.Cells[row, 6].Text,
                            AccountName = worksheet.Cells[row, 4].Text,
                            CreatedBy = worksheet.Cells[row, 2].Text,
                            CreatedDate = DateTime.TryParse(worksheet.Cells[row, 3].Text, out DateTime createdDate)
                                ? createdDate
                                : default,
                            OriginalBankId = int.TryParse(worksheet.Cells[row, 7].Text, out int originalBankId)
                                ? originalBankId
                                : 0,
                        };

                        if (bankAccountList.Any(ba => ba.OriginalBankId == bankAccount.OriginalBankId))
                        {
                            continue;
                        }

                        await _dbContext.BankAccounts.AddAsync(bankAccount, cancellationToken);
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (OperationCanceledException oce)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = oce.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.BankAccount });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    TempData["error"] = ex.Message;
                    return RedirectToAction(nameof(Index), new { view = DynamicView.BankAccount });
                }
            }

            TempData["success"] = "Uploading Success!";
            return RedirectToAction(nameof(Index), new { view = DynamicView.BankAccount });
        }

        #endregion -- import xlsx record --
    }
}
