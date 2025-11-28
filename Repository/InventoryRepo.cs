using Accounting_System.Data;
using Accounting_System.Models;
using Accounting_System.Models.AccountsPayable;
using Accounting_System.Models.AccountsReceivable;
using Accounting_System.Models.Reports;
using Accounting_System.Models.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Accounting_System.Repository
{
    public class InventoryRepo
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly JournalVoucherRepo _journalVoucherRepo;
        private readonly GeneralRepo _generalRepo;

        public InventoryRepo(ApplicationDbContext dbContext, UserManager<IdentityUser> userManager, JournalVoucherRepo journalVoucherRepo, GeneralRepo generalRepo)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _journalVoucherRepo = journalVoucherRepo;
            _generalRepo = generalRepo;
        }

        //public async Task UpdateQuantity(decimal sold, int productId, CancellationToken cancellationToken = default)
        //{
        //    var inventory = await _dbContext
        //        .Inventories
        //        .FirstOrDefaultAsync(x => x.ProductId == productId, cancellationToken);
        //    if (inventory == null)
        //    {
        //        throw new ArgumentException("No product found !");
        //    }
        //    inventory.QuantityServe += sold;
        //    inventory.QuantityBalance -= sold;

        //    await _dbContext.SaveChangesAsync(cancellationToken);
        //}

        public async Task<bool> HasAlreadyBeginningInventory(int productId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Inventories
                .AnyAsync(i => i.ProductId == productId, cancellationToken);
        }

        public async Task AddBeginningInventory(BeginningInventoryViewModel viewModel, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(user.Identity!.Name!);
            Inventory inventory = new()
            {
                Date = viewModel.Date,
                ProductId = viewModel.ProductId,
                Quantity = viewModel.Quantity,
                Cost = viewModel.Cost,
                Particular = "Beginning Balance",
                Total = viewModel.Quantity * viewModel.Cost,
                InventoryBalance = viewModel.Quantity,
                AverageCost = viewModel.Cost,
                TotalBalance = viewModel.Quantity * viewModel.Cost,
                IsValidated = true,
                ValidatedBy = createdBy,
                ValidatedDate = DateTime.Now
            };

            await _dbContext.Inventories.AddAsync(inventory, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task AddPurchaseToInventoryAsync(ReceivingReport receivingReport, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(user.Identity!.Name!);
            var sortedInventory = await _dbContext.Inventories
            .Where(i => i.ProductId == receivingReport.PurchaseOrder!.Product!.ProductId)
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

            var lastIndex = sortedInventory.FindLastIndex(s => s.Date <= receivingReport.Date);
            if (lastIndex >= 0)
            {
                sortedInventory = sortedInventory.Skip(lastIndex).ToList();
            }

            var previousInventory = sortedInventory.FirstOrDefault();

            decimal total = receivingReport.QuantityReceived * Math.Round(receivingReport.PurchaseOrder!.Price / 1.12m, 2);
            decimal inventoryBalance = lastIndex >= 0 ? previousInventory!.InventoryBalance + receivingReport.QuantityReceived : receivingReport.QuantityReceived;
            decimal totalBalance = lastIndex >= 0 ? previousInventory!.TotalBalance + total : total;
            decimal averageCost = totalBalance != 0 && inventoryBalance != 0 ? totalBalance / inventoryBalance : previousInventory!.Cost;

            Inventory inventory = new()
            {
                Date = receivingReport.Date,
                ProductId = receivingReport.PurchaseOrder.ProductId,
                Particular = "Purchases",
                Reference = receivingReport.ReceivingReportNo,
                Quantity = receivingReport.QuantityReceived,
                Cost = receivingReport.PurchaseOrder.Price / 1.12m, //unit cost
                IsValidated = true,
                ValidatedBy = createdBy,
                ValidatedDate = DateTime.Now,
                Total = total,
                InventoryBalance = inventoryBalance,
                TotalBalance = totalBalance,
                AverageCost = averageCost
            };


            foreach (var transaction in sortedInventory.Skip(1))
            {
                if (transaction.Particular == "Sales")
                {
                    transaction.Cost = averageCost;
                    transaction.Total = transaction.Quantity * averageCost;
                    transaction.TotalBalance = totalBalance != 0 ? totalBalance - transaction.Total : transaction.Total;
                    transaction.InventoryBalance = inventoryBalance != 0 ? inventoryBalance - transaction.Quantity : transaction.Quantity;
                    transaction.AverageCost = transaction.TotalBalance != 0 && transaction.InventoryBalance != 0 ? transaction.TotalBalance / transaction.InventoryBalance : averageCost;
                    var costOfGoodsSold = transaction.AverageCost * transaction.Quantity;

                    averageCost = transaction.AverageCost;
                    totalBalance = transaction.TotalBalance;
                    inventoryBalance = transaction.InventoryBalance;

                    var journalEntries = await _dbContext.GeneralLedgerBooks
                        .Where(j => j.Reference == transaction.Reference &&
                                    (j.AccountNo.StartsWith("50101") || j.AccountNo.StartsWith("10104")))
                        .ToListAsync(cancellationToken);

                    if (journalEntries.Count != 0)
                    {
                        foreach (var journal in journalEntries)
                        {
                            if (journal.Debit != 0)
                            {
                                if (journal.Debit != costOfGoodsSold)
                                {
                                    journal.Debit = costOfGoodsSold;
                                    journal.Credit = 0;
                                }
                            }
                            else
                            {
                                if (journal.Credit != costOfGoodsSold)
                                {
                                    journal.Credit = costOfGoodsSold;
                                    journal.Debit = 0;
                                }
                            }
                        }
                    }

                    _dbContext.GeneralLedgerBooks.UpdateRange(journalEntries);
                }
                else if (transaction.Particular == "Purchases")
                {
                    transaction.TotalBalance = totalBalance + transaction.Total;
                    transaction.InventoryBalance = inventoryBalance + transaction.Quantity;
                    transaction.AverageCost = transaction.TotalBalance != 0 && transaction.InventoryBalance != 0 ? transaction.TotalBalance / transaction.InventoryBalance : averageCost;

                    averageCost = transaction.AverageCost;
                    totalBalance = transaction.TotalBalance;
                    inventoryBalance = transaction.InventoryBalance;
                }
            }

            _dbContext.Inventories.UpdateRange(sortedInventory);

            await _dbContext.AddAsync(inventory, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task AddSalesToInventoryAsync(SalesInvoice salesInvoice, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(user.Identity!.Name!);
            var sortedInventory = await _dbContext.Inventories
            .Where(i => i.ProductId == salesInvoice.Product!.ProductId)
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

            var lastIndex = sortedInventory.FindLastIndex(s => s.Date <= salesInvoice.TransactionDate);
            if (lastIndex >= 0)
            {
                sortedInventory = sortedInventory.Skip(lastIndex).ToList();
            }
            else
            {
                throw new ArgumentException($"Beginning inventory for {salesInvoice.Product!.ProductName} not found!");
            }

            var previousInventory = sortedInventory.FirstOrDefault();

            if (previousInventory != null)
            {
                if (previousInventory.InventoryBalance < salesInvoice.Quantity)
                {
                    throw new InvalidOperationException($"The quantity exceeds the available inventory of '{salesInvoice.Product!.ProductName}'.");
                }

                decimal total = salesInvoice.Quantity * previousInventory.AverageCost;
                decimal inventoryBalance = previousInventory.InventoryBalance - salesInvoice.Quantity;
                decimal totalBalance = previousInventory.TotalBalance - total;
                decimal averageCost = inventoryBalance != 0 && totalBalance != 0 ? totalBalance / inventoryBalance : previousInventory.AverageCost;

                Inventory inventory = new()
                {
                    Date = salesInvoice.TransactionDate,
                    ProductId = salesInvoice.Product!.ProductId,
                    Particular = "Sales",
                    Reference = salesInvoice.SalesInvoiceNo,
                    Quantity = salesInvoice.Quantity,
                    Cost = previousInventory.AverageCost,
                    IsValidated = true,
                    ValidatedBy = createdBy,
                    ValidatedDate = DateTime.Now,
                    Total = total,
                    InventoryBalance = inventoryBalance,
                    TotalBalance = totalBalance,
                    AverageCost = averageCost
                };


                foreach (var transaction in sortedInventory.Skip(1))
                {
                    if (transaction.Particular == "Sales")
                    {
                        transaction.Cost = averageCost;
                        transaction.Total = transaction.Quantity * averageCost;
                        transaction.TotalBalance = totalBalance != 0 ? totalBalance - transaction.Total : transaction.Total;
                        transaction.InventoryBalance = inventoryBalance != 0 ? inventoryBalance - transaction.Quantity : transaction.Quantity;
                        transaction.AverageCost = transaction.TotalBalance != 0 && transaction.InventoryBalance != 0 ? transaction.TotalBalance / transaction.InventoryBalance : averageCost;
                        var costOfGoodsSold = transaction.AverageCost * transaction.Quantity;

                        averageCost = transaction.AverageCost;
                        totalBalance = transaction.TotalBalance;
                        inventoryBalance = transaction.InventoryBalance;

                        var journalEntries = await _dbContext.GeneralLedgerBooks
                            .Where(j => j.Reference == transaction.Reference &&
                                        (j.AccountNo.StartsWith("50101") || j.AccountNo.StartsWith("10104")))
                            .ToListAsync(cancellationToken);

                        if (journalEntries.Count != 0)
                        {
                            foreach (var journal in journalEntries)
                            {
                                if (journal.Debit != 0)
                                {
                                    if (journal.Debit != costOfGoodsSold)
                                    {
                                        journal.Debit = costOfGoodsSold;
                                        journal.Credit = 0;
                                    }
                                }
                                else
                                {
                                    if (journal.Credit != costOfGoodsSold)
                                    {
                                        journal.Credit = costOfGoodsSold;
                                        journal.Debit = 0;
                                    }
                                }
                            }
                        }

                        _dbContext.GeneralLedgerBooks.UpdateRange(journalEntries);

                    }
                    else if (transaction.Particular == "Purchases")
                    {
                        transaction.TotalBalance = totalBalance + transaction.Total;
                        transaction.InventoryBalance = inventoryBalance + transaction.Quantity;
                        transaction.AverageCost = transaction.TotalBalance != 0 && transaction.InventoryBalance != 0 ? transaction.TotalBalance / transaction.InventoryBalance : averageCost;

                        averageCost = transaction.AverageCost;
                        totalBalance = transaction.TotalBalance;
                        inventoryBalance = transaction.InventoryBalance;
                    }
                }

                var ledgers = new List<GeneralLedgerBook>
                {
                    new GeneralLedgerBook
                    {
                        Date = salesInvoice.TransactionDate,
                        Reference = salesInvoice.SalesInvoiceNo!,
                        Description = salesInvoice.Product.ProductName,
                        AccountNo = salesInvoice.Product.ProductCode == "PET001" ? "501010100" : salesInvoice.Product.ProductCode == "PET002" ? "501010200" : "501010300",
                        AccountTitle = salesInvoice.Product.ProductCode == "PET001" ? "COGS - Biodiesel" : salesInvoice.Product.ProductCode == "PET002" ? "COGS - Econogas" : "COGS - Envirogas",
                        Debit = inventory.Total,
                        Credit = 0,
                        CreatedBy = salesInvoice.CreatedBy,
                        CreatedDate = salesInvoice.CreatedDate
                    },
                    new GeneralLedgerBook
                    {
                        Date = salesInvoice.TransactionDate,
                        Reference = salesInvoice.SalesInvoiceNo!,
                        Description = salesInvoice.Product.ProductName,
                        AccountNo = salesInvoice.Product.ProductCode == "PET001" ? "101040100" : salesInvoice.Product.ProductCode == "PET002" ? "101040200" : "101040300",
                        AccountTitle = salesInvoice.Product.ProductCode == "PET001" ? "Inventory - Biodiesel" : salesInvoice.Product.ProductCode == "PET002" ? "Inventory - Econogas" : "Inventory - Envirogas",
                        Debit = 0,
                        Credit = inventory.Total,
                        CreatedBy = salesInvoice.CreatedBy,
                        CreatedDate = salesInvoice.CreatedDate
                    }
                };

                _dbContext.Inventories.UpdateRange(sortedInventory);

                if (_generalRepo.IsJournalEntriesBalanced(ledgers))
                {
                    await _dbContext.Inventories.AddAsync(inventory, cancellationToken);
                    await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledgers, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                else
                {
                    throw new ArgumentException("Debit and Credit is not equal, check your entries.");
                }


            }
            else
            {
                throw new InvalidOperationException($"Beginning inventory for this product '{salesInvoice.Product!.ProductName}' not found!");
            }
        }

        public async Task AddActualInventory(ActualInventoryViewModel viewModel, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(user.Identity!.Name!);
            #region -- Actual Inventory Entry --

            var total = viewModel.Variance * viewModel.AverageCost;
            var inventoryBalance = viewModel.Variance + viewModel.PerBook;
            var totalBalance = viewModel.TotalBalance + total;
            var particular = viewModel.Variance < 0 ? "Actual Inventory(Loss)" : "Actual Inventory(Gain)";

            Inventory inventory = new()
            {
                Date = viewModel.Date,
                ProductId = viewModel.ProductId,
                Quantity = Math.Abs(viewModel.Variance),
                Cost = viewModel.AverageCost,
                Particular = particular,
                Total = Math.Abs(total),
                InventoryBalance = inventoryBalance,
                AverageCost = totalBalance / inventoryBalance,
                TotalBalance = totalBalance
            };
            await _dbContext.Inventories.AddAsync(inventory, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            #endregion -- Actual Inventory Entry --

            #region -- General Book Entry --

            var ledger = new List<GeneralLedgerBook>();
            for (int i = 0; i < viewModel.AccountNumber.Length; i++)
            {
                ledger.Add(
                    new GeneralLedgerBook
                    {
                        Date = viewModel.Date,
                        Reference = inventory.Id.ToString(),
                        AccountNo = viewModel.AccountNumber[i],
                        AccountTitle = viewModel.AccountTitle[i],
                        Description = particular,
                        Debit = Math.Abs(viewModel.Debit[i]),
                        Credit = Math.Abs(viewModel.Credit[i]),
                        CreatedBy = createdBy,
                        CreatedDate = DateTime.Now,
                        IsPosted = false
                    });
            }

            await _dbContext.GeneralLedgerBooks.AddRangeAsync(ledger, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            #endregion -- General Book Entry --
        }

        public async Task ChangePriceToInventoryAsync(PurchaseChangePriceViewModel purchaseChangePriceViewModel, ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var createdBy = await _generalRepo.GetUserFullNameAsync(user.Identity!.Name!);
            var existingPo = await _dbContext.PurchaseOrders
                .Include(po => po.Supplier).Include(purchaseOrder => purchaseOrder.Product)
                .FirstOrDefaultAsync(po => po.PurchaseOrderId == purchaseChangePriceViewModel.POId, cancellationToken);


            var previousInventory = await _dbContext.Inventories
                .Where(i => i.ProductId == existingPo!.ProductId)
                .OrderByDescending(i => i.Date)
                .ThenByDescending(i => i.Id)
                .Include(i => i.Product)
                .FirstOrDefaultAsync(cancellationToken);

            var previousInventoryList = await _dbContext.Inventories
                .Where(i => i.ProductId == existingPo!.ProductId)
                .OrderByDescending(i => i.Date)
                .ThenByDescending(i => i.Id)
                .ToListAsync(cancellationToken: cancellationToken);

            var findRr = await _dbContext.ReceivingReports
                .Where(rr => rr.PurchaseOrder!.ProductId == previousInventory!.ProductId)
                .ToListAsync(cancellationToken);
            if (previousInventory != null && previousInventoryList.Any())
            {
                #region -- Inventory Entry --

                var generateJvNo = await _journalVoucherRepo.GenerateJVNo(cancellationToken);

                Inventory inventory = new()
                {
                    Date = DateOnly.FromDateTime(DateTime.Now),
                    ProductId = existingPo!.ProductId,
                    Particular = "Change Price",
                    Reference = generateJvNo,
                    Quantity = 0,
                    Cost = 0,
                    IsValidated = true,
                    ValidatedBy = createdBy,
                    ValidatedDate = DateTime.Now
                };

                purchaseChangePriceViewModel.FinalPrice /= 1.12m;
                var newTotal = (purchaseChangePriceViewModel.FinalPrice - previousInventory.AverageCost) * previousInventory.InventoryBalance;

                inventory.Total = newTotal;
                inventory.InventoryBalance = previousInventory.InventoryBalance;
                inventory.TotalBalance = previousInventory.TotalBalance + newTotal;
                inventory.AverageCost = inventory.TotalBalance / inventory.InventoryBalance;

                decimal computeRrTotalAmount = findRr.Sum(rr => rr.Amount);
                decimal productAmount = newTotal < 0 ? newTotal / 1.12m : (computeRrTotalAmount + newTotal) / 1.12m;
                decimal vatInput = productAmount * 0.12m;
                decimal wht = productAmount * 0.01m;
                decimal apTradePayable = newTotal < 0 ? newTotal - wht : (computeRrTotalAmount + newTotal) - wht;

                #endregion -- Inventory Entry --

                #region -- Journal Voucher Entry --

                var journalVoucherHeader = new List<JournalVoucherHeader>
                {
                    new JournalVoucherHeader
                    {
                        Date = DateOnly.FromDateTime(DateTime.Now),
                        JournalVoucherHeaderNo = generateJvNo,
                        References = "",
                        Particulars = $"Change price of {existingPo.PurchaseOrderNo} from {existingPo.Price} to {existingPo.FinalPrice }",
                        CRNo = "",
                        JVReason = "Change Price",
                        CreatedBy = createdBy,
                        CreatedDate = DateTime.Now,
                        IsPosted = true,
                        PostedDate = DateTime.Now
                    },
                };

                #region -- JV Detail Entry --

                if (inventory.Total > 0)
                {
                    #region -- JV Detail Entry --

                    var journalVoucherDetail = new List<JournalVoucherDetail>
                    {
                        new JournalVoucherDetail
                        {
                            AccountNo = previousInventory.Product.ProductCode == "PET001" ? "101040100" : previousInventory.Product.ProductCode == "PET002" ? "101040200" : "101040300",
                            AccountName = previousInventory.Product.ProductCode == "PET001" ? "Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "Inventory - Econogas" : "Inventory - Envirogas",
                            TransactionNo = generateJvNo,
                            Debit = Math.Abs(productAmount),
                            Credit = 0
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "101060200",
                            AccountName = "Vat - Input",
                            TransactionNo = generateJvNo,
                            Debit = Math.Abs(vatInput),
                            Credit = 0
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "201030210",
                            AccountName = "Expanded Withholding Tax 1%",
                            TransactionNo = generateJvNo,
                            Debit = 0,
                            Credit = Math.Abs(wht)
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "202010100",
                            AccountName = "AP-Trade Payable",
                            TransactionNo = generateJvNo,
                            Debit = 0,
                            Credit = Math.Abs(apTradePayable)
                        }
                    };

                    await _dbContext.AddRangeAsync(journalVoucherDetail, cancellationToken);

                    #endregion -- JV Detail Entry --
                }
                else
                {
                    #region -- JV Detail Entry --

                    var journalVoucherDetail = new List<JournalVoucherDetail>
                    {
                        new JournalVoucherDetail
                        {
                            AccountNo = previousInventory.Product.ProductCode == "PET001" ? "101040100" : previousInventory.Product.ProductCode == "PET002" ? "101040200" : "101040300",
                            AccountName = previousInventory.Product.ProductCode == "PET001" ? "Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "Inventory - Econogas" : "Inventory - Envirogas",
                            TransactionNo = generateJvNo,
                            Debit = 0,
                            Credit = Math.Abs(productAmount)
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "101060200",
                            AccountName = "Vat - Input",
                            TransactionNo = generateJvNo,
                            Debit = 0,
                            Credit = Math.Abs(vatInput)
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "201030210",
                            AccountName = "Expanded Withholding Tax 1%",
                            TransactionNo = generateJvNo,
                            Debit = Math.Abs(wht),
                            Credit = 0
                        },
                        new JournalVoucherDetail
                        {
                            AccountNo = "202010100",
                            AccountName = "AP-Trade Payable",
                            TransactionNo = generateJvNo,
                            Debit = Math.Abs(apTradePayable),
                            Credit = 0
                        }
                    };

                    await _dbContext.AddRangeAsync(journalVoucherDetail, cancellationToken);

                    #endregion -- JV Detail Entry --
                }

                #endregion -- JV Detail Entry --

                await _dbContext.AddRangeAsync(journalVoucherHeader, cancellationToken);

                #endregion -- Journal Voucher Entry --

                #region -- Journal Book Entry --

                if (inventory.Total > 0)
                {
                    #region -- Journal Book Entry --

                    var journalBook = new List<JournalBook>
                    {
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = previousInventory.Product.ProductCode == "PET001" ? "101040100 Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "101040200 Inventory - Econogas" : "101040300 Inventory - Envirogas",
                            Debit = Math.Abs(productAmount),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "101060200 Vat - Input",
                            Debit = Math.Abs(vatInput),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "201030210 Expanded Withholding Tax 1%",
                            Debit = 0,
                            Credit = Math.Abs(wht),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "202010100 AP-Trade Payable",
                            Debit = 0,
                            Credit = Math.Abs(apTradePayable),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        }
                    };
                    await _dbContext.AddRangeAsync(journalBook, cancellationToken);

                    #endregion -- Journal Book Entry --
                }
                else
                {
                    #region -- Journal Book Entry --

                    var journalBook = new List<JournalBook>
                    {
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = previousInventory.Product.ProductCode == "PET001" ? "101040100 Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "101040200 Inventory - Econogas" : "101040300 Inventory - Envirogas",
                            Debit = 0,
                            Credit = Math.Abs(productAmount),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "101060200 Vat - Input",
                            Debit = 0,
                            Credit = Math.Abs(vatInput),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "201030210 Expanded Withholding Tax 1%",
                            Debit = Math.Abs(wht),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new JournalBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountTitle = "202010100 AP-Trade Payable",
                            Debit = Math.Abs(apTradePayable),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        }
                    };
                    await _dbContext.AddRangeAsync(journalBook, cancellationToken);

                    #endregion -- Journal Book Entry --
                }

                #endregion -- Journal Book Entry --

                #region -- Purchase Book Entry --

                var purchaseBook = new List<PurchaseJournalBook>
                    {
                        new PurchaseJournalBook
                        {
                            Date = existingPo.Date,
                            SupplierName = existingPo.Supplier!.SupplierName,
                            SupplierTin = existingPo.Supplier.SupplierTin,
                            SupplierAddress = existingPo.Supplier.SupplierAddress,
                            DocumentNo = "",
                            Description = existingPo.Product!.ProductName,
                            Discount = 0,
                            VatAmount = inventory.Total * 0.12m,
                            Amount = newTotal,
                            WhtAmount = inventory.Total * 0.01m,
                            NetPurchases = inventory.Total,
                            PONo = existingPo.PurchaseOrderNo!,
                            DueDate = DateOnly.FromDateTime(DateTime.MinValue),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        }
                    };

                #endregion -- Purchase Book Entry --

                #region -- General Book Entry --

                if (inventory.Total > 0)
                {
                    #region -- General Book Entry --

                    var ledgers = new List<GeneralLedgerBook>
                    {
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = previousInventory.Product.ProductCode == "PET001" ? "101040100" : previousInventory.Product.ProductCode == "PET002" ? "101040200" : "101040300",
                            AccountTitle = previousInventory.Product.ProductCode == "PET001" ? "Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "Inventory - Econogas" : "Inventory - Envirogas",
                            Debit = Math.Abs(productAmount),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "101060200",
                            AccountTitle = "Vat - Input",
                            Debit = Math.Abs(vatInput),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "201030210",
                            AccountTitle = "Expanded Withholding Tax 1%",
                            Debit = 0,
                            Credit = Math.Abs(wht),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "202010100",
                            AccountTitle = "AP-Trade Payable",
                            Debit = 0,
                            Credit = Math.Abs(apTradePayable),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        }
                    };
                    await _dbContext.AddRangeAsync(ledgers, cancellationToken);

                    #endregion -- General Book Entry --
                }
                else
                {
                    #region -- General Book Entry --

                    var ledgers = new List<GeneralLedgerBook>
                    {
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = previousInventory.Product.ProductCode == "PET001" ? "101040100" : previousInventory.Product.ProductCode == "PET002" ? "101040200" : "101040300",
                            AccountTitle = previousInventory.Product.ProductCode == "PET001" ? "Inventory - Biodiesel" : previousInventory.Product.ProductCode == "PET002" ? "Inventory - Econogas" : "Inventory - Envirogas",
                            Debit = 0,
                            Credit = Math.Abs(productAmount),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "101060200",
                            AccountTitle = "Vat - Input",
                            Debit = 0,
                            Credit = Math.Abs(vatInput),
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "201030210",
                            AccountTitle = "Expanded Withholding Tax 1%",
                            Debit = Math.Abs(wht),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        },
                        new GeneralLedgerBook
                        {
                            Date = existingPo.Date,
                            Reference = inventory.Reference,
                            Description = "Change Price",
                            AccountNo = "202010100",
                            AccountTitle = "AP-Trade Payable",
                            Debit = Math.Abs(apTradePayable),
                            Credit = 0,
                            CreatedBy = createdBy,
                            CreatedDate = DateTime.Now
                        }
                    };
                    await _dbContext.AddRangeAsync(ledgers, cancellationToken);

                    #endregion -- General Book Entry --
                }

                await _dbContext.AddRangeAsync(purchaseBook, cancellationToken);
                await _dbContext.AddAsync(inventory, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                #endregion -- General Book Entry --
            }
            else
            {
                throw new InvalidOperationException($"Beginning inventory for this product '{existingPo!.Product!.ProductName}' not found!");
            }
        }

        public async Task VoidInventory(Inventory model, CancellationToken cancellationToken = default)
        {
            var sortedInventory = await _dbContext.Inventories
            .Where(i => i.ProductId == model.ProductId && i.Date >= model.Date)
            .OrderBy(i => i.Date)
            .ThenBy(i => i.Id)
            .ToListAsync(cancellationToken);

            sortedInventory.Remove(model);

            if (sortedInventory.Count != 0)
            {
                var previousInventory = sortedInventory.FirstOrDefault();

                decimal inventoryBalance = previousInventory!.InventoryBalance;
                decimal totalBalance = previousInventory.TotalBalance;
                decimal averageCost = inventoryBalance == 0 && totalBalance == 0 ? previousInventory.AverageCost : totalBalance / inventoryBalance;

                foreach (var transaction in sortedInventory.Skip(1))
                {
                    if (transaction.Particular == "Sales")
                    {
                        transaction.Cost = averageCost;
                        transaction.Total = transaction.Quantity * averageCost;
                        transaction.TotalBalance = totalBalance - transaction.Total;
                        transaction.InventoryBalance = inventoryBalance - transaction.Quantity;
                        transaction.AverageCost = transaction.TotalBalance == 0 && transaction.InventoryBalance == 0 ? previousInventory.AverageCost : transaction.TotalBalance / transaction.InventoryBalance;
                        var costOfGoodsSold = transaction.AverageCost * transaction.Quantity;

                        averageCost = transaction.AverageCost;
                        totalBalance = transaction.TotalBalance;
                        inventoryBalance = transaction.InventoryBalance;

                        var journalEntries = await _dbContext.GeneralLedgerBooks
                            .Where(j => j.Reference == transaction.Reference &&
                                        (j.AccountNo.StartsWith("50101") || j.AccountNo.StartsWith("10104")))
                            .ToListAsync(cancellationToken);

                        if (journalEntries.Count != 0)
                        {
                            foreach (var journal in journalEntries)
                            {
                                if (journal.Debit != 0)
                                {
                                    if (journal.Debit != costOfGoodsSold)
                                    {
                                        journal.Debit = costOfGoodsSold;
                                        journal.Credit = 0;
                                    }
                                }
                                else
                                {
                                    if (journal.Credit != costOfGoodsSold)
                                    {
                                        journal.Credit = costOfGoodsSold;
                                        journal.Debit = 0;
                                    }
                                }
                            }
                        }

                        _dbContext.GeneralLedgerBooks.UpdateRange(journalEntries);

                    }
                    else if (transaction.Particular == "Purchases")
                    {
                        transaction.TotalBalance = totalBalance + transaction.Total;
                        transaction.InventoryBalance = inventoryBalance + transaction.Quantity;
                        transaction.AverageCost = transaction.TotalBalance / transaction.InventoryBalance;

                        averageCost = transaction.AverageCost;
                        totalBalance = transaction.TotalBalance;
                        inventoryBalance = transaction.InventoryBalance;
                    }
                }

                _dbContext.Inventories.UpdateRange(sortedInventory);
                _dbContext.Inventories.Remove(model);

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
