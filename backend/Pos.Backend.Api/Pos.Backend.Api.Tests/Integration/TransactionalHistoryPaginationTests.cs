using System.Text;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class TransactionalHistoryPaginationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime SharedInstant =
        new(2026, 9, 18, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly SharedBusinessDate = new(2026, 9, 18);

    public Task InitializeAsync() => database.ResetDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sales_pages_are_stable_filtered_tenant_scoped_and_bounded()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "sales-page", 101, 50m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "sales-other", 102, 50m);
        var sales = await SeedSalesAsync(tenant, 7, "sales-page", matchingCount: 4);
        await SeedSalesAsync(otherTenant, 3, "sales-other", matchingCount: 3);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.Sales.GetSalesAsync(new SaleListQueryDto { Page = 1, PageSize = 3 });
        var second = await services.Sales.GetSalesAsync(new SaleListQueryDto { Page = 2, PageSize = 3 });
        var third = await services.Sales.GetSalesAsync(new SaleListQueryDto { Page = 3, PageSize = 3 });
        var actualIds = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).ToArray();
        var expectedIds = sales.OrderByDescending(sale => sale.Id).Select(sale => sale.Id).ToArray();

        Assert.Equal(7, first.TotalItems);
        Assert.Equal(3, first.TotalPages);
        Assert.Null(first.Summary);
        Assert.Equal(expectedIds, actualIds);
        Assert.Equal(actualIds.Length, actualIds.Distinct().Count());

        var sorted = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Page = 1,
            PageSize = 20,
            SortBy = "total",
            SortDirection = "asc"
        });
        Assert.Equal(
            sales.OrderBy(sale => sale.Total).ThenBy(sale => sale.Id).Select(sale => sale.Id),
            sorted.Items.Select(item => item.Id));

        var filtered = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Search = "needle",
            Page = 1,
            PageSize = 2
        });
        Assert.Equal(4, filtered.TotalItems);
        Assert.Equal(2, filtered.TotalPages);
        Assert.All(filtered.Items, item => Assert.Contains("needle", item.Notes));

        var minimumBounds = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Page = 0,
            PageSize = 0
        });
        var maximumBound = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Page = 1,
            PageSize = 999
        });
        Assert.Equal(1, minimumBounds.Page);
        Assert.Equal(1, minimumBounds.PageSize);
        Assert.Single(minimumBounds.Items);
        Assert.Equal(200, maximumBound.PageSize);
        Assert.Equal(7, maximumBound.Items.Count);
    }

    [Fact]
    public async Task Sales_summary_is_global_on_every_page_and_preserves_credit_note_semantics()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "sales-summary", 103, 50m);
        Sale creditedSale;

        await using (var context = database.CreateDbContext())
        {
            creditedSale = CreateSale(
                tenant,
                "SUM-001",
                total: 100m,
                subtotal: 100m,
                totalCost: 60m,
                SaleStatus.Completed,
                SaleDocumentType.Invoice,
                SaleDocumentStatus.Authorized);
            var sales = new[]
            {
                creditedSale,
                CreateSale(tenant, "SUM-002", 50m, 50m, 30m),
                CreateSale(
                    tenant,
                    "SUM-003",
                    30m,
                    30m,
                    10m,
                    SaleStatus.Completed,
                    SaleDocumentType.Invoice,
                    SaleDocumentStatus.Draft),
                CreateSale(
                    tenant,
                    "SUM-004",
                    25m,
                    25m,
                    15m,
                    SaleStatus.Voided)
            };
            context.Sales.AddRange(sales);
            await context.SaveChangesAsync();

            context.CreditNotes.Add(new CreditNote
            {
                CompanyId = tenant.CompanyId,
                EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = tenant.EmissionPointId,
                UserId = tenant.UserId,
                OriginalSaleId = creditedSale.Id,
                DocumentStatus = SaleDocumentStatus.Authorized,
                AccessKey = new string('1', 49),
                AuthorizationNumber = "AUTH-SUM-001",
                SriAuthorizationStatus = "AUTORIZADO",
                InventoryReturnedAt = SharedInstant.AddHours(1),
                InventoryReturnedByUserId = tenant.UserId,
                Reason = "Authorized return",
                GrossSubtotal = 20m,
                Subtotal = 20m,
                Vat0Subtotal = 20m,
                Total = 20m,
                BusinessDate = SharedBusinessDate,
                TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
                CreatedAt = SharedInstant.AddHours(1),
                Items =
                {
                    new CreditNoteItem
                    {
                        ProductId = tenant.Products[0].Id,
                        ProductNameSnapshot = "Returned product",
                        ProductMainCodeSnapshot = "RET-001",
                        Quantity = 1m,
                        UnitPrice = 20m,
                        UnitCost = 12m,
                        GrossSubtotal = 20m,
                        NetSubtotal = 20m,
                        LineSubtotal = 20m,
                        VatCategory = ProductVatCategory.Vat0,
                        TaxableSubtotal = 20m,
                        LineTotal = 20m,
                        LineCost = 12m
                    }
                }
            });
            await context.SaveChangesAsync();
        }

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Page = 1,
            PageSize = 2,
            IncludeSummary = true
        });
        var second = await services.Sales.GetSalesAsync(new SaleListQueryDto
        {
            Page = 2,
            PageSize = 2,
            IncludeSummary = true
        });
        var summary = Assert.IsType<SaleReportSummaryDto>(first.Summary);
        var secondSummary = Assert.IsType<SaleReportSummaryDto>(second.Summary);

        Assert.Equal(summary.SalesCount, secondSummary.SalesCount);
        Assert.Equal(summary.NetTotal, secondSummary.NetTotal);
        Assert.Equal(summary.NetGrossMarginPercent, secondSummary.NetGrossMarginPercent);
        Assert.Equal(4, summary.SalesCount);
        Assert.Equal(180m, summary.TotalSold);
        Assert.Equal(20m, summary.AuthorizedCreditNoteTotal);
        Assert.Equal(1, summary.AuthorizedCreditNoteCount);
        Assert.Equal(160m, summary.NetTotal);
        Assert.Equal(88m, summary.NetCost);
        Assert.Equal(72m, summary.NetGrossProfit);
        Assert.Equal(45m, summary.NetGrossMarginPercent);
        Assert.Equal(2, summary.InvoiceCount);
        Assert.Equal(2, summary.TicketCount);
        Assert.Equal(1, summary.VoidedCount);
        Assert.Equal(1, summary.AuthorizedCount);

        var creditedRow = first.Items.Concat(second.Items).Single(item => item.Id == creditedSale.Id);
        Assert.Equal(1, creditedRow.CreditNoteImpact.AuthorizedCreditNoteCount);
        Assert.Equal(20m, creditedRow.CreditNoteImpact.AuthorizedCreditNoteTotal);
        Assert.Equal(80m, creditedRow.CreditNoteImpact.NetTotal);
        Assert.Equal(48m, creditedRow.CreditNoteImpact.NetCost);
        Assert.Equal(32m, creditedRow.CreditNoteImpact.NetGrossProfit);
        Assert.Equal(40m, creditedRow.CreditNoteImpact.NetGrossMarginPercent);
    }

    [Fact]
    public async Task Sales_export_contains_the_complete_filtered_result_with_excel_safe_csv()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "sales-export", 104, 50m);
        var sales = await SeedSalesAsync(tenant, 5, "sales-export", matchingCount: 5);

        await using (var context = database.CreateDbContext())
        {
            var first = await context.Sales.SingleAsync(sale => sale.Id == sales[0].Id);
            first.Total = 1.15m;
            first.Subtotal = 1.15m;
            first.GrossProfit = 1.15m - first.TotalCost;
            first.Notes = "exportable; \"quoted\"\nline";
            await context.SaveChangesAsync();
        }

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var export = await services.Sales.ExportSalesAsync(new SaleListQueryDto
        {
            Search = "exportable",
            Page = 1,
            PageSize = 2
        });
        var csv = Encoding.UTF8.GetString(export.Content);

        Assert.Equal([0xef, 0xbb, 0xbf], export.Content.Take(3).ToArray());
        Assert.StartsWith("\uFEFFID;Fecha;Documento;Cliente", csv);
        Assert.Contains(sales[0].Number!, csv);
        Assert.Contains(sales[^1].Number!, csv);
        Assert.Contains(";1,15;", csv);
        Assert.Contains("\"exportable; \"\"quoted\"\"\nline\"", csv);
        Assert.DoesNotContain('$', csv);
    }

    [Fact]
    public async Task Purchase_receipts_are_stable_filtered_tenant_scoped_and_summarized()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "purchases-page", 105, 50m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "purchases-other", 106, 50m);
        var receipts = await SeedPurchaseReceiptsAsync(tenant, "purchases-page", postedCount: 5, canceledCount: 2);
        await SeedPurchaseReceiptsAsync(otherTenant, "purchases-other", postedCount: 2, canceledCount: 1);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var first = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Page = 1,
            PageSize = 3
        });
        var second = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Page = 2,
            PageSize = 3
        });
        var third = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Page = 3,
            PageSize = 3
        });
        var actualIds = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).ToArray();
        var expectedIds = receipts.OrderByDescending(receipt => receipt.Id).Select(receipt => receipt.Id).ToArray();

        Assert.Equal(expectedIds, actualIds);
        Assert.Equal(7, first.TotalItems);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(5, first.Summary.PostedCount);
        Assert.Equal(2, first.Summary.CanceledCount);
        Assert.Equal(50m, first.Summary.TotalReceived);

        var filtered = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Search = "needle",
            Page = 1,
            PageSize = 1
        });
        Assert.Equal(2, filtered.TotalItems);
        Assert.Equal(2, filtered.TotalPages);

        var canceled = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Status = PurchaseReceiptStatus.Canceled,
            Page = 1,
            PageSize = 20
        });
        Assert.Equal(2, canceled.TotalItems);
        Assert.Equal(0, canceled.Summary.PostedCount);
        Assert.Equal(2, canceled.Summary.CanceledCount);
        Assert.Equal(0m, canceled.Summary.TotalReceived);

        var bounds = await services.PurchaseReceipts.GetListAsync(new PurchaseReceiptListQueryDto
        {
            Page = -1,
            PageSize = 999
        });
        Assert.Equal(1, bounds.Page);
        Assert.Equal(200, bounds.PageSize);
    }

    [Fact]
    public async Task Cash_sessions_are_stable_tenant_scoped_and_keep_open_live_and_closed_snapshot_totals()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "cash-page", 107, 50m);
        var otherTenant = await TestDataBuilder.CreateTenantAsync(database, "cash-other", 108, 50m);
        var sessions = await SeedCashSessionsAsync(tenant, "cash-page", openCount: 3, closedCount: 3);
        await SeedCashSessionsAsync(otherTenant, "cash-other", openCount: 1, closedCount: 1);

        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var pages = new List<CashSessionListItemDto>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
            {
                Page = page,
                PageSize = 2
            });
            pages.AddRange(result.Items);
            Assert.Equal(6, result.TotalItems);
            Assert.Equal(3, result.Summary.OpenCount);
            Assert.Equal(3, result.Summary.ClosedCount);
        }

        var expectedIds = sessions.OrderByDescending(session => session.Id).Select(session => session.Id).ToArray();
        Assert.Equal(expectedIds, pages.Select(item => item.Id).ToArray());
        Assert.Equal(pages.Count, pages.Select(item => item.Id).Distinct().Count());

        var openSession = sessions.First(session => session.Status == CashSessionStatus.Open);
        var openRow = pages.Single(item => item.Id == openSession.Id);
        Assert.Equal(5m, openRow.CashSalesAmount);
        Assert.Equal(7m, openRow.CardSalesAmount);
        Assert.Equal(2m, openRow.CashInAmount);
        Assert.Equal(1m, openRow.CashOutAmount);
        Assert.Equal(16m, openRow.ExpectedCashAmount);

        var closedSession = sessions.First(session => session.Status == CashSessionStatus.Closed);
        var closedRow = pages.Single(item => item.Id == closedSession.Id);
        Assert.Equal(11m, closedRow.CashSalesAmount);
        Assert.Equal(30m, closedRow.ExpectedCashAmount);

        var filtered = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
        {
            Status = CashSessionStatus.Open,
            Page = 1,
            PageSize = 20
        });
        Assert.Equal(3, filtered.TotalItems);
        Assert.Equal(3, filtered.Summary.OpenCount);
        Assert.Equal(0, filtered.Summary.ClosedCount);

        var byUser = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
        {
            UserId = openSession.OpenedByUserId,
            Page = 1,
            PageSize = 20
        });
        Assert.Single(byUser.Items);
        Assert.Equal(openSession.Id, byUser.Items[0].Id);

        var bounds = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
        {
            Page = 0,
            PageSize = 0
        });
        Assert.Equal(1, bounds.Page);
        Assert.Equal(1, bounds.PageSize);
    }

    [Fact]
    public async Task Cash_open_session_list_uses_a_constant_number_of_queries()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "cash-query-count", 109, 50m);
        await SeedCashSessionsAsync(tenant, "cash-query-count", openCount: 6, closedCount: 0);
        var counter = new CommandCountingInterceptor();

        await using var services = new TestServiceScope(database, tenant.OperationalContext, counter);
        counter.Reset();
        var oneSession = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
        {
            Status = CashSessionStatus.Open,
            Page = 1,
            PageSize = 1
        });
        var oneSessionQueryCount = counter.ReaderCount;

        counter.Reset();
        var sixSessions = await services.CashSessions.GetListAsync(new CashSessionListQueryDto
        {
            Status = CashSessionStatus.Open,
            Page = 1,
            PageSize = 200
        });
        var sixSessionQueryCount = counter.ReaderCount;

        Assert.Single(oneSession.Items);
        Assert.Equal(6, sixSessions.Items.Count);
        Assert.Equal(oneSessionQueryCount, sixSessionQueryCount);
        Assert.Equal(4, sixSessionQueryCount);
    }

    private async Task<List<Sale>> SeedSalesAsync(
        TestTenant tenant,
        int count,
        string key,
        int matchingCount)
    {
        await using var context = database.CreateDbContext();
        var sales = Enumerable.Range(1, count)
            .Select(index =>
            {
                var sale = CreateSale(
                    tenant,
                    $"{key.ToUpperInvariant()}-{index:000}",
                    total: index * 10m,
                    subtotal: index * 10m,
                    totalCost: index * 4m);
                sale.Notes = index <= matchingCount ? $"needle exportable {index}" : $"other {index}";
                return sale;
            })
            .ToList();

        context.Sales.AddRange(sales);
        await context.SaveChangesAsync();
        return sales;
    }

    private static Sale CreateSale(
        TestTenant tenant,
        string number,
        decimal total,
        decimal subtotal,
        decimal totalCost,
        SaleStatus status = SaleStatus.Completed,
        SaleDocumentType documentType = SaleDocumentType.Ticket,
        SaleDocumentStatus documentStatus = SaleDocumentStatus.NotRequired)
        => new()
        {
            CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId,
            UserId = tenant.UserId,
            Status = status,
            PaymentMethod = SalePaymentMethod.Cash,
            DocumentType = documentType,
            DocumentStatus = documentStatus,
            Number = number,
            BuyerNameSnapshot = "Test buyer",
            BuyerIdentificationSnapshot = "0999999999",
            GrossSubtotal = subtotal,
            Subtotal = subtotal,
            Vat0Subtotal = subtotal,
            Total = total,
            TotalCost = totalCost,
            GrossProfit = subtotal - totalCost,
            GrossMarginPercent = subtotal > 0m ? (subtotal - totalCost) / subtotal * 100m : 0m,
            BusinessDate = SharedBusinessDate,
            TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
            CreatedAt = SharedInstant
        };

    private async Task<List<PurchaseReceipt>> SeedPurchaseReceiptsAsync(
        TestTenant tenant,
        string key,
        int postedCount,
        int canceledCount)
    {
        await using var context = database.CreateDbContext();
        var supplier = new Supplier
        {
            CompanyId = tenant.CompanyId,
            Name = $"Supplier {key}",
            Identification = $"SUP-{key}",
            IsActive = true,
            CreatedAt = SharedInstant
        };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();

        var receipts = Enumerable.Range(1, postedCount + canceledCount)
            .Select(index =>
            {
                var canceled = index > postedCount;
                return new PurchaseReceipt
                {
                    CompanyId = tenant.CompanyId,
                    EstablishmentId = tenant.EstablishmentId,
                    SupplierId = supplier.Id,
                    ReceiptNumber = $"{key.ToUpperInvariant()}-{index:000}",
                    SupplierDocumentNumber = $"DOC-{index:000}",
                    ReceiptDate = SharedInstant,
                    ReceiptBusinessDate = SharedBusinessDate,
                    ReceiptTimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
                    Status = canceled ? PurchaseReceiptStatus.Canceled : PurchaseReceiptStatus.Posted,
                    Subtotal = 10m,
                    Notes = index <= 2 ? $"needle {index}" : $"other {index}",
                    CreatedAt = SharedInstant,
                    CreatedByUserId = tenant.UserId,
                    PostedAt = SharedInstant,
                    CanceledAt = canceled ? SharedInstant.AddHours(1) : null,
                    CanceledBusinessDate = canceled ? SharedBusinessDate : null,
                    CanceledTimeZoneIdSnapshot = canceled
                        ? tenant.OperationalContext.CompanyTimeZoneId
                        : null,
                    CanceledByUserId = canceled ? tenant.UserId : null,
                    CancelReason = canceled ? "Test cancellation" : null
                };
            })
            .ToList();

        context.PurchaseReceipts.AddRange(receipts);
        await context.SaveChangesAsync();
        return receipts;
    }

    private async Task<List<CashSession>> SeedCashSessionsAsync(
        TestTenant tenant,
        string key,
        int openCount,
        int closedCount)
    {
        await using var context = database.CreateDbContext();
        var primaryUser = await context.Users.AsNoTracking().SingleAsync(user => user.Id == tenant.UserId);
        var users = Enumerable.Range(1, openCount + closedCount)
            .Select(index => new User
            {
                Username = $"{key}-user-{index}",
                Email = $"{key}-user-{index}@hfpos.test",
                PasswordHash = "test-only-password-hash",
                IsActive = true,
                CreatedAt = SharedInstant,
                RoleId = primaryUser.RoleId,
                CompanyId = tenant.CompanyId,
                EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = tenant.EmissionPointId
            })
            .ToList();
        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        var sessions = new List<CashSession>();
        for (var index = 0; index < openCount + closedCount; index++)
        {
            var isOpen = index < openCount;
            sessions.Add(new CashSession
            {
                CompanyId = tenant.CompanyId,
                EstablishmentId = tenant.EstablishmentId,
                EmissionPointId = tenant.EmissionPointId,
                OpenedByUserId = users[index].Id,
                ClosedByUserId = isOpen ? null : users[index].Id,
                Status = isOpen ? CashSessionStatus.Open : CashSessionStatus.Closed,
                OpeningAmount = isOpen ? 10m : 20m,
                ExpectedCashAmount = isOpen ? 0m : 30m,
                CountedCashAmount = isOpen ? null : 31m,
                DifferenceAmount = isOpen ? null : 1m,
                CashSalesAmount = isOpen ? 0m : 11m,
                CardSalesAmount = isOpen ? 0m : 3m,
                TransferSalesAmount = isOpen ? 0m : 4m,
                OtherSalesAmount = isOpen ? 0m : 5m,
                CashInAmount = isOpen ? 0m : 1m,
                CashOutAmount = isOpen ? 0m : 2m,
                OpenedAt = SharedInstant,
                OpenBusinessDate = SharedBusinessDate,
                OpenTimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId,
                ClosedAt = isOpen ? null : SharedInstant.AddHours(8),
                ClosedBusinessDate = isOpen ? null : SharedBusinessDate,
                ClosedTimeZoneIdSnapshot = isOpen
                    ? null
                    : tenant.OperationalContext.CompanyTimeZoneId
            });
        }

        context.CashSessions.AddRange(sessions);
        await context.SaveChangesAsync();

        foreach (var session in sessions.Where(session => session.Status == CashSessionStatus.Open))
        {
            context.Sales.Add(CreateCashSale(tenant, session.Id, SalePaymentMethod.Cash, 5m));
            context.Sales.Add(CreateCashSale(tenant, session.Id, SalePaymentMethod.Card, 7m));
            context.CashMovements.Add(CreateCashMovement(tenant, session.Id, CashMovementType.CashIn, 2m));
            context.CashMovements.Add(CreateCashMovement(tenant, session.Id, CashMovementType.CashOut, 1m));
        }

        var closedSession = sessions.FirstOrDefault(session => session.Status == CashSessionStatus.Closed);
        if (closedSession is not null)
        {
            context.Sales.Add(CreateCashSale(tenant, closedSession.Id, SalePaymentMethod.Cash, 999m));
            context.CashMovements.Add(
                CreateCashMovement(tenant, closedSession.Id, CashMovementType.CashIn, 999m));
        }

        await context.SaveChangesAsync();
        return sessions;
    }

    private static Sale CreateCashSale(
        TestTenant tenant,
        int cashSessionId,
        SalePaymentMethod paymentMethod,
        decimal total)
    {
        var sale = CreateSale(
            tenant,
            $"CASH-{cashSessionId}-{paymentMethod}",
            total,
            total,
            totalCost: 0m);
        sale.CashSessionId = cashSessionId;
        sale.PaymentMethod = paymentMethod;
        return sale;
    }

    private static CashMovement CreateCashMovement(
        TestTenant tenant,
        int cashSessionId,
        CashMovementType type,
        decimal amount)
        => new()
        {
            CashSessionId = cashSessionId,
            CompanyId = tenant.CompanyId,
            EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId,
            UserId = tenant.UserId,
            Type = type,
            Amount = amount,
            Reason = "Pagination integration test",
            CreatedAt = SharedInstant,
            BusinessDate = SharedBusinessDate,
            TimeZoneIdSnapshot = tenant.OperationalContext.CompanyTimeZoneId
        };
}
