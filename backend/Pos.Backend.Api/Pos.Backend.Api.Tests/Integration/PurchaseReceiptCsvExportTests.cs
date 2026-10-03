using System.Text;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PurchaseReceiptCsvExportTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private static readonly DateTime Instant = new(2026, 9, 18, 2, 0, 0, DateTimeKind.Utc);
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Complete_export_ignores_page_bounds_and_is_one_projected_query()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "export-all", 522, 1m);
        var receipts = await SeedAsync(tenant, 205);
        var counter = new CommandCountingInterceptor();
        await using var services = new TestServiceScope(database, tenant.OperationalContext, counter);
        var list = await services.PurchaseReceipts.GetListAsync(new() { Page = 2, PageSize = 1 });
        Assert.Single(list.Items);
        counter.Reset();
        var export = await services.PurchaseReceipts.ExportAsync(new() { Page = 2, PageSize = 1 });
        Assert.Equal(1, counter.ReaderCount);
        Assert.Equal(ExpectedIds(receipts), Ids(export));
        Assert.Equal(205, Ids(export).Length);
        Assert.StartsWith("recepciones-compra-", export.FileName);
        Assert.EndsWith(".csv", export.FileName);
        Assert.Equal("text/csv; charset=utf-8", export.ContentType);
        Assert.Contains(";17/09/2026 21:00;18/09/2026 21:00;cashier-export-all;Synthetic cancellation",
            Encoding.UTF8.GetString(export.Content));
    }

    [Theory]
    [InlineData(" supplier ")]
    [InlineData(" RECEIPT ")]
    [InlineData(" DOCUMENT ")]
    [InlineData(" NOTES ")]
    public async Task Search_matches_all_existing_fields_with_list_semantics(string search)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "export-search", 522, 1m);
        var receipts = await SeedAsync(tenant, 5);
        await using (var context = database.CreateDbContext())
        {
            var unrelatedSupplier = new Supplier
            {
                CompanyId = tenant.CompanyId, Name = "Unrelated", CreatedAt = Instant, IsActive = true
            };
            context.Suppliers.Add(unrelatedSupplier);
            await context.SaveChangesAsync();
            var unrelated = NewReceipt(tenant, unrelatedSupplier.Id, 99);
            unrelated.ReceiptNumber = unrelated.SupplierDocumentNumber = unrelated.Notes = "Unrelated";
            context.PurchaseReceipts.Add(unrelated);
            await context.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var query = new PurchaseReceiptListQueryDto { Search = search, PageSize = 1 };
        var list = await services.PurchaseReceipts.GetListAsync(query);
        var export = await services.PurchaseReceipts.ExportAsync(query);
        Assert.Equal(5, list.TotalItems);
        Assert.Equal(ExpectedIds(receipts), Ids(export));
    }

    [Theory]
    [InlineData(18, null, null)]
    [InlineData(null, 17, null)]
    [InlineData(17, 18, 1)]
    [InlineData(17, 18, 2)]
    public async Task Dates_are_inclusive_business_dates_and_status_matches_list(int? from, int? to, int? status)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "export-dates", 522, 1m);
        var receipts = await SeedAsync(tenant, 8);
        var query = new PurchaseReceiptListQueryDto
        {
            From = from.HasValue ? new DateTime(2026, 9, from.Value) : null,
            To = to.HasValue ? new DateTime(2026, 9, to.Value) : null,
            Status = status.HasValue ? (PurchaseReceiptStatus)status.Value : null,
            PageSize = 1
        };
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var list = await services.PurchaseReceipts.GetListAsync(query);
        var export = await services.PurchaseReceipts.ExportAsync(query);
        var expected = receipts.Where(r => (!from.HasValue || r.ReceiptBusinessDate.Day >= from)
            && (!to.HasValue || r.ReceiptBusinessDate.Day <= to)
            && (!status.HasValue || (int)r.Status == status)).ToList();
        Assert.Equal(expected.Count, list.TotalItems);
        Assert.Equal(ExpectedIds(expected), Ids(export));
    }

    [Fact]
    public async Task Export_excludes_other_companies_and_establishments_and_preserves_order()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "export-scope", 522, 1m);
        var other = await TestDataBuilder.CreateTenantAsync(database, "export-foreign", 523, 1m);
        var receipts = await SeedAsync(tenant, 8);
        await SeedAsync(other, 3);
        await using (var context = database.CreateDbContext())
        {
            var establishment = new Establishment
            {
                CompanyId = tenant.CompanyId, Code = "002", Name = "Other establishment",
                Address = "Synthetic address", CreatedAt = Instant, IsActive = true
            };
            context.Establishments.Add(establishment);
            await context.SaveChangesAsync();
            var outside = NewReceipt(tenant, receipts[0].SupplierId, 99);
            outside.EstablishmentId = establishment.Id;
            context.PurchaseReceipts.Add(outside);
            await context.SaveChangesAsync();
        }
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var export = await services.PurchaseReceipts.ExportAsync(new());
        Assert.Equal(ExpectedIds(receipts), Ids(export));
    }

    [Fact]
    public async Task Empty_filtered_export_is_a_valid_header_only_file()
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, "export-empty", 522, 1m);
        await SeedAsync(tenant, 3);
        await using var services = new TestServiceScope(database, tenant.OperationalContext);
        var export = await services.PurchaseReceipts.ExportAsync(new() { Search = "no match exists" });
        Assert.Empty(Ids(export));
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, export.Content.Take(3));
    }

    private async Task<List<PurchaseReceipt>> SeedAsync(TestTenant tenant, int count)
    {
        await using var context = database.CreateDbContext();
        var supplier = new Supplier
        {
            CompanyId = tenant.CompanyId, Name = "Supplier needle", IsActive = true, CreatedAt = Instant
        };
        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync();
        var rows = Enumerable.Range(1, count).Select(i => NewReceipt(tenant, supplier.Id, i)).ToList();
        context.PurchaseReceipts.AddRange(rows);
        await context.SaveChangesAsync();
        return rows;
    }

    private static PurchaseReceipt NewReceipt(TestTenant tenant, int supplierId, int i) => new()
    {
        CompanyId = tenant.CompanyId, EstablishmentId = tenant.EstablishmentId, SupplierId = supplierId,
        ReceiptNumber = $"Receipt-{i}", SupplierDocumentNumber = $"Document-{i}", Notes = $"Notes-{i}",
        ReceiptBusinessDate = new DateOnly(2026, 9, 17 + i % 3),
        ReceiptDate = Instant.AddHours(i % 2), ReceiptTimeZoneIdSnapshot = "America/Guayaquil",
        Subtotal = 10.25m, CreatedAt = Instant, CreatedByUserId = tenant.UserId,
        Status = i % 2 == 0 ? PurchaseReceiptStatus.Canceled : PurchaseReceiptStatus.Posted,
        CanceledAt = i % 2 == 0 ? Instant.AddDays(1) : null,
        CanceledBusinessDate = i % 2 == 0 ? new DateOnly(2026, 9, 18) : null,
        CanceledTimeZoneIdSnapshot = i % 2 == 0 ? "America/Guayaquil" : null,
        CanceledByUserId = i % 2 == 0 ? tenant.UserId : null,
        CancelReason = i % 2 == 0 ? "Synthetic cancellation" : null
    };

    private static int[] ExpectedIds(IEnumerable<PurchaseReceipt> rows) => rows
        .OrderByDescending(r => r.ReceiptBusinessDate).ThenByDescending(r => r.ReceiptDate)
        .ThenByDescending(r => r.Id).Select(r => r.Id).ToArray();

    private static int[] Ids(PurchaseReceiptCsvExportDto export) => Encoding.UTF8.GetString(export.Content)
        .Split("\r\n").Skip(1).Select(line => int.Parse(line.Split(';')[0])).ToArray();
}
