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

        private readonly GeneralRepo _generalRepo;

        public BankAccountController(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager,
            BankAccountRepo bankAccountRepo, GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _bankAccountRepo = bankAccountRepo;
            _generalRepo = generalRepo;
        }

        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            var ba = await _bankAccountRepo.GetBankAccountAsync(cancellationToken);

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
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
                try
                {
                    if (await _bankAccountRepo.IsBankAccountNameExist(model.AccountName, cancellationToken))
                    {
                        ModelState.AddModelError("AccountName", "Bank account name already exist!");
                        return View(model);
                    }

                    model.CreatedBy = createdBy;

                    #region --Audit Trail Recording

                    if (model.OriginalBankId == 0)
                    {
                        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                        AuditTrail auditTrailBook = new(createdBy, $"Created new bank {model.AccountName}",
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
                 return RedirectToAction(nameof(Index));
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
                var createdBy = await _generalRepo.GetUserFullNameAsync(User.Identity!.Name!);
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
                            AuditTrail auditTrailBook = new(createdBy,
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
    }
}
