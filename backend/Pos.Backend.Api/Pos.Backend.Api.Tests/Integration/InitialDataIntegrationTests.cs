using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;
using Pos.Backend.Api.WebApi.Controllers;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class InitialDataIntegrationTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Five_datasets_preview_create_replay_and_audit_without_overwriting_masters()
    {
        var tenant = await TenantAsync("complete", 701);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var service = Service(scope, tenant);
        var inputs = new[]
        {
            Payload("categories", "name\n\" Initial, category \"\n"),
            Payload("products", "internalCode,barcode,name,category,price,cost,minimumStock,vatCategory\nNEW-1,BAR-1,Product,\"Initial, category\",12.34,1.2345,0.1250,Vat5\n"),
            Payload("customers", "name,identificationType,identification,phone,email,address,notes\nCustomer,06,PASSPORT701,,customer@synthetic.test,Test address,\"note, quoted\"\n"),
            Payload("suppliers", "name,identification,phone,email,address,notes\nSupplier,SUP701,,supplier@synthetic.test,Test address,\n"),
            Payload("opening-inventory", "internalCode,quantity\nNEW-1,8.1250\n")
        };
        foreach (var input in inputs)
        {
            Assert.StartsWith(input.Kind == "opening-inventory" ? "internalCode" : input.Kind == "products" ? "internalCode" : "name",
                await service.GetTemplateAsync(input.Kind));
            var preview = await service.PreviewAsync(input);
            Assert.True(preview.CanConfirm, string.Join(',', preview.Errors.Concat(preview.Rows.SelectMany(r => r.Errors))));
            var result = await service.ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! });
            Assert.Equal(result.BatchId, (await service.ConfirmAsync(new() { Payload = input, PreviewToken = "expired-or-lost-token" })).BatchId);
            Assert.Equal(tenant.CompanyId, result.CompanyId);
            Assert.Equal(tenant.UserId, result.UserId);
            Assert.Equal([2], result.RowNumbers);
            Assert.Single(result.CreatedIds);
            Assert.False((await service.PreviewAsync(Payload(input.Kind, input.Csv))).CanConfirm);
        }
        await using var verify = database.CreateDbContext();
        Assert.Equal(5, await verify.InitialDataBatches.CountAsync());
        var product = await verify.Products.SingleAsync(p => p.InternalCode == "NEW-1");
        Assert.Equal(12.34m, product.Price); Assert.Equal(1.2345m, product.Cost);
        Assert.Equal(ProductVatCategory.Vat5, product.VatCategory);
        Assert.NotNull(product.CurrentCostEventId);
        var movement = await verify.InventoryMovements.SingleAsync();
        Assert.Equal(InventoryMovementSourceType.OpeningInventory, movement.SourceType);
        Assert.Equal(tenant.UserId, movement.UserId); Assert.Equal(8.125m, movement.StockAfter);
        Assert.DoesNotContain("customer@", (await verify.InitialDataBatches.SingleAsync(b => b.Kind == "customers")).ResultJson);
    }

    [Theory]
    [InlineData("categories", "name\nSame\nsame\n", "DUPLICATE_CREATE_ONLY")]
    [InlineData("products", "internalCode,barcode,name,category,price,cost,minimumStock,vatCategory\nP,,N,Missing,1.234,2,3,Vat15\n", "price:INVALID_DECIMAL")]
    [InlineData("customers", "name,identificationType,identification,phone,email,address,notes\nName,05,bad,,bad-email,,\n", "identification:INVALID")]
    [InlineData("suppliers", "name,identification,phone,email,address,notes\nSupplier,,,,,\n", "identification:INVALID")]
    [InlineData("opening-inventory", "internalCode,quantity\nunknown,-1\n", "quantity:INVALID_DECIMAL")]
    public async Task Invalid_previews_report_rows_and_never_mutate(string kind, string csv, string error)
    {
        var tenant = await TenantAsync("invalid", 702);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var result = await Service(scope, tenant).PreviewAsync(Payload(kind, csv));
        Assert.False(result.CanConfirm); Assert.Null(result.PreviewToken);
        Assert.Contains(error, result.Rows.SelectMany(r => r.Errors));
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.InitialDataBatches.ToListAsync());
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
        Assert.Single(await verify.Products.ToListAsync());
    }

    [Fact]
    public async Task Csv_limits_header_and_malformed_quotes_are_bounded()
    {
        var tenant = await TenantAsync("limits", 703);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var service = Service(scope, tenant);
        Assert.Contains("CSV_HEADER_INVALID", (await service.PreviewAsync(Payload("categories", "other\nValue"))).Errors);
        Assert.Contains("CSV_SIZE_INVALID", (await service.PreviewAsync(Payload("categories", new string('a', 1048577)))).Errors);
        var limit = await service.PreviewAsync(Payload("categories", "name\n" + string.Join('\n', Enumerable.Range(1, 501).Select(i => "C" + i))));
        Assert.Equal(500, limit.Rows.Count); Assert.Contains("CSV_ROW_LIMIT", limit.Errors);
        var malformed = await service.PreviewAsync(Payload("categories", "name\n\"unclosed"));
        Assert.Contains("CSV_MALFORMED", malformed.Rows.SelectMany(r => r.Errors));
        Assert.True((await service.PreviewAsync(Payload("categories", "\uFEFFname\r\n New \r\n"))).CanConfirm);
    }

    [Fact]
    public async Task Concurrent_confirmation_creates_one_batch_and_payload_change_conflicts()
    {
        var tenant = await TenantAsync("concurrent", 704);
        var input = Payload("opening-inventory", "internalCode,quantity\nconcurrent-P1,10\n");
        await using var previewScope = new TestServiceScope(database, tenant.OperationalContext);
        var preview = await Service(previewScope, tenant).PreviewAsync(input);
        async Task<InitialDataResultDto> Confirm()
        {
            await using var scope = new TestServiceScope(database, tenant.OperationalContext);
            return await Service(scope, tenant).ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! });
        }
        var results = await Task.WhenAll(Confirm(), Confirm()).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(results[0].BatchId, results[1].BatchId);
        input.Csv = input.Csv.Replace("10", "11");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(previewScope, tenant)
            .ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }));
        Assert.Equal("INITIAL_DATA_REQUEST_CONFLICT", ex.Message);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.InitialDataBatches.ToListAsync());
        Assert.Single(await verify.InventoryMovements.ToListAsync());
        Assert.Equal(10m, await TestDataBuilder.GetStockAsync(verify, tenant, tenant.Products[0].Id));
    }

    [Fact]
    public async Task Failed_second_opening_row_rolls_back_ledger_stock_and_movements()
    {
        var tenant = await TenantAsync("rollback", 705, 0m, 0m);
        var input = Payload("opening-inventory", "internalCode,quantity\nrollback-P1,10\nrollback-P2,20\n");
        await using var previewScope = new TestServiceScope(database, tenant.OperationalContext);
        var preview = await Service(previewScope, tenant).PreviewAsync(input);
        await using var fail = new TestServiceScope(database, tenant.OperationalContext, new FailSecondMovement(tenant.Products[1].Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(fail, tenant)
            .ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }));
        await using var verify = database.CreateDbContext();
        Assert.Empty(await verify.InitialDataBatches.ToListAsync());
        Assert.Empty(await verify.InventoryMovements.ToListAsync());
        Assert.All(await verify.ProductStocks.ToListAsync(), s => Assert.Equal(0m, s.Quantity));
    }

    [Fact]
    public async Task Preview_is_tenant_actor_and_context_bound_and_foreign_references_are_not_resolved()
    {
        var a = await TenantAsync("a", 706);
        var b = await TenantAsync("b", 707);
        await using var scopeA = new TestServiceScope(database, a.OperationalContext);
        await using var scopeB = new TestServiceScope(database, b.OperationalContext);
        var input = Payload("categories", "name\nPrivate category\n");
        var preview = await Service(scopeA, a).PreviewAsync(input);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Service(scopeB, b)
            .ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }));
        Assert.Equal("INITIAL_DATA_PREVIEW_INVALID", ex.Message);
        var foreign = await Service(scopeB, b).PreviewAsync(Payload("opening-inventory", "internalCode,quantity\na-P1,1\n"));
        Assert.Null(foreign.Rows[0].ResolvedId); Assert.False(foreign.CanConfirm);
        await using var db = database.CreateDbContext();
        var role = await db.Users.Where(u => u.Id == a.UserId).Select(u => u.Role).SingleAsync();
        db.RolePermissions.RemoveRange(await db.RolePermissions.Where(r => r.RoleId == role.Id).ToListAsync());
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<OperationalContextException>(() => Service(scopeA, a)
            .ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }));
    }

    [Fact]
    public async Task Movement_after_preview_rejects_opening_even_after_balance_returns_to_zero()
    {
        var tenant = await TenantAsync("opening-stale", 708);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var input = Payload("opening-inventory", "internalCode,quantity\nopening-stale-P1,20\n");
        var service = Service(scope, tenant);
        var preview = await service.PreviewAsync(input);
        await scope.Inventory.RegisterEntryAsync(new() { RequestId = Guid.NewGuid(), ProductId = tenant.Products[0].Id, Quantity = 2m });
        await scope.Inventory.RegisterExitAsync(new() { RequestId = Guid.NewGuid(), ProductId = tenant.Products[0].Id, Quantity = 2m });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }));
        Assert.Equal("INITIAL_DATA_REVALIDATION_FAILED", ex.Message);
        Assert.Empty(await scope.DbContext.InitialDataBatches.ToListAsync());
    }

    [Fact]
    public async Task Stale_count_detects_same_balance_and_fresh_count_uses_domain_movements()
    {
        var tenant = await TenantAsync("count", 709, 10m);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var snapshot = (await scope.Inventory.GetProductStockAsync(tenant.Products[0].Id))!;
        await scope.Inventory.RegisterExitAsync(new() { RequestId = Guid.NewGuid(), ProductId = snapshot.ProductId, Quantity = 2m });
        await scope.Inventory.RegisterEntryAsync(new() { RequestId = Guid.NewGuid(), ProductId = snapshot.ProductId, Quantity = 2m });
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Inventory.RegisterAdjustmentAsync(Count(snapshot, 12m)));
        Assert.Equal("INVENTORY_SNAPSHOT_STALE", ex.Message);
        snapshot = (await scope.Inventory.GetProductStockAsync(snapshot.ProductId))!;
        var result = await scope.Inventory.RegisterAdjustmentAsync(Count(snapshot, 12m));
        Assert.Equal(10m, result.StockBefore); Assert.Equal(12m, result.StockAfter);
        var wrongContext = Count((await scope.Inventory.GetProductStockAsync(snapshot.ProductId))!, 15m);
        wrongContext.ExpectedEstablishmentId = tenant.EstablishmentId + 10;
        Assert.Equal("INVENTORY_SNAPSHOT_STALE", (await Assert.ThrowsAsync<InvalidOperationException>(() => scope.Inventory.RegisterAdjustmentAsync(wrongContext))).Message);
    }

    [Fact]
    public async Task Missing_stock_snapshot_and_concurrent_counts_are_safe()
    {
        var tenant = await TenantAsync("missing", 710);
        await using (var db = database.CreateDbContext())
        { db.ProductStocks.Remove(await db.ProductStocks.SingleAsync()); await db.SaveChangesAsync(); }
        await using var previewScope = new TestServiceScope(database, tenant.OperationalContext);
        var snapshot = (await previewScope.Inventory.GetProductStockAsync(tenant.Products[0].Id))!;
        Assert.Equal(0m, snapshot.Quantity); Assert.Equal(0, snapshot.MovementWatermark);
        // Existing company lock excludes other stock writers while both contenders create a missing row.
        async Task<string> Attempt()
        {
            await using var scope = new TestServiceScope(database, tenant.OperationalContext);
            try { await scope.Inventory.RegisterAdjustmentAsync(Count(snapshot, 4m)); return "OK"; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        var results = await Task.WhenAll(Attempt(), Attempt()).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains("OK", results); Assert.Contains("INVENTORY_SNAPSHOT_STALE", results);
    }

    [Fact]
    public async Task Manual_customer_creation_shares_company_lock_and_import_revalidates_duplicate()
    {
        var tenant = await TenantAsync("manual", 711);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var input = Payload("customers", "name,identificationType,identification,phone,email,address,notes\nCustomer,06,DUP711,,,,\n");
        var service = Service(scope, tenant); var preview = await service.PreviewAsync(input);
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        await using var manualScope = new TestServiceScope(database, tenant.OperationalContext);
        var controller = new CustomersController(manualScope.DbContext, accessor, new CustomerQueryService(manualScope.DbContext, accessor));
        async Task<string> Import()
        {
            try { await service.ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }); return "IMPORTED"; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        var manual = controller.Create(new() { Name = "Manual", IdentificationType = "06", Identification = "DUP711" });
        var import = Import();
        await Task.WhenAll(manual, import).WaitAsync(TimeSpan.FromSeconds(30));
        if (manual.Result.Result is CreatedAtActionResult) Assert.Equal("INITIAL_DATA_REVALIDATION_FAILED", import.Result);
        else { Assert.IsType<ConflictObjectResult>(manual.Result.Result); Assert.Equal("IMPORTED", import.Result); }
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Customers.ToListAsync());
    }

    [Fact]
    public async Task Product_import_persists_explicit_zero_minimum_and_retains_default_three()
    {
        var tenant = await TenantAsync("minimum", 715);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var category = await scope.DbContext.Categories.SingleAsync();
        var input = Payload("products", $"internalCode,barcode,name,category,price,cost,minimumStock,vatCategory\nZERO,,Zero,{category.Name},1,0,0,Vat15\nTHREE,,Three,{category.Name},1,0,3,Vat15\n");
        var service = Service(scope, tenant);
        var preview = await service.PreviewAsync(input);
        Assert.True(preview.CanConfirm);
        await service.ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! });
        await using var verify = database.CreateDbContext();
        Assert.Equal(0m, (await verify.Products.SingleAsync(p => p.InternalCode == "ZERO")).MinimumStock);
        Assert.Equal(3m, (await verify.Products.SingleAsync(p => p.InternalCode == "THREE")).MinimumStock);
        var defaultProduct = new Product { CompanyId = tenant.CompanyId, CategoryId = category.Id,
            InternalCode = "DEFAULT", Name = "Synthetic default", Price = 1m, IsActive = true, CreatedAt = DateTime.UtcNow };
        verify.Products.Add(defaultProduct);
        await verify.SaveChangesAsync();
        verify.ChangeTracker.Clear();
        Assert.Equal(3m, (await verify.Products.SingleAsync(p => p.InternalCode == "DEFAULT")).MinimumStock);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Manual_supplier_and_import_serialize_before_supplier_reads(bool manualFirst, bool update)
    {
        var tenant = await TenantAsync("supplier-race", 716);
        var originalId = 0;
        if (update)
        {
            await using var setup = database.CreateDbContext();
            var original = new Supplier { CompanyId = tenant.CompanyId, Name = "Synthetic original",
                Identification = "OLD716", IsActive = true, CreatedAt = DateTime.UtcNow };
            setup.Suppliers.Add(original); await setup.SaveChangesAsync(); originalId = original.Id;
        }
        var input = Payload("suppliers", "name,identification,phone,email,address,notes\nImported,RACE716,,,,\n");
        await using var previewScope = new TestServiceScope(database, tenant.OperationalContext);
        var preview = await Service(previewScope, tenant).PreviewAsync(input);
        var firstLock = new HoldCompanyLock(); var secondLock = new ObserveCompanyLock();
        await using var manualScope = new TestServiceScope(database, tenant.OperationalContext, manualFirst ? firstLock : secondLock);
        await using var importScope = new TestServiceScope(database, tenant.OperationalContext, manualFirst ? secondLock : firstLock);
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        var controller = new SuppliersController(manualScope.DbContext, accessor, new SupplierQueryService(manualScope.DbContext, accessor));
        async Task<string> Manual()
        {
            var result = update
                ? await controller.Update(originalId, new() { Name = "Manual", Identification = "RACE716", IsActive = true })
                : (await controller.Create(new() { Name = "Manual", Identification = "RACE716" })).Result;
            return result is ConflictObjectResult ? "CONFLICT" : result is NoContentResult or CreatedAtActionResult ? "MANUAL" : "UNEXPECTED";
        }
        async Task<string> Import()
        {
            try { await Service(importScope, tenant).ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! }); return "IMPORTED"; }
            catch (InvalidOperationException ex) { return ex.Message; }
        }
        var first = manualFirst ? Manual() : Import();
        Task<string>? second = null;
        try
        {
            await firstLock.Acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = manualFirst ? Import() : Manual();
            await secondLock.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Prove the contender is waiting on this owner in PostgreSQL, not just scheduled nearby.
            await WaitUntilBlockedAsync(firstLock.BackendPid, secondLock.BackendPid);
        }
        finally { firstLock.Release.TrySetResult(); }
        var results = await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(manualFirst ? ["MANUAL", "INITIAL_DATA_REVALIDATION_FAILED"] : ["IMPORTED", "CONFLICT"], results);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.Suppliers.Where(s => s.Identification == "RACE716").ToListAsync());
        Assert.Equal(manualFirst ? 0 : 1, await verify.InitialDataBatches.CountAsync());
        if (update && !manualFirst)
            Assert.Equal("OLD716", (await verify.Suppliers.SingleAsync(s => s.Id == originalId)).Identification);
    }

    [Fact]
    public async Task Manual_supplier_rechecks_session_after_waiting_for_company_lock()
    {
        var tenant = await TenantAsync("supplier-revoke", 717);
        tenant.OperationalContext.UserSessionVersion = 1; tenant.OperationalContext.RoleAuthorizationVersion = 1;
        await using var lockDb = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(lockDb).BeginChangeAsync(tenant.CompanyId);
        var entered = new ObserveCompanyLock();
        await using var scope = new TestServiceScope(database, tenant.OperationalContext, entered);
        var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        var controller = new SuppliersController(scope.DbContext, accessor, new SupplierQueryService(scope.DbContext, accessor));
        var pending = controller.Create(new() { Name = "Manual", Identification = "REVOKED717" });
        await entered.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await lockDb.Users.Where(u => u.Id == tenant.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionVersion, 2L));
        await tx.CommitAsync();
        var ex = await Assert.ThrowsAsync<OperationalContextException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("SESSION_STALE", ex.ErrorCode);
        Assert.Empty(await scope.DbContext.Suppliers.ToListAsync());
    }

    [Fact]
    public async Task Exact_category_duplicates_are_rejected_even_when_existing_category_is_inactive()
    {
        var tenant = await TenantAsync("category-unique", 718);
        await using var db = database.CreateDbContext();
        var original = await db.Categories.SingleAsync();
        original.IsActive = false; await db.SaveChangesAsync();
        db.Categories.Add(new Category { CompanyId = tenant.CompanyId, Name = original.Name, IsActive = true, CreatedAt = DateTime.UtcNow });
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var postgres = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
        Assert.Equal("IX_Categories_CompanyId_Name", postgres.ConstraintName);
    }

    [Fact]
    public async Task Session_revoked_while_confirmation_waits_for_company_lock_is_rejected()
    {
        var tenant = await TenantAsync("revoke", 712);
        tenant.OperationalContext.UserSessionVersion = 1; tenant.OperationalContext.RoleAuthorizationVersion = 1;
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var input = Payload("categories", "name\nRevocation category\n");
        var preview = await Service(scope, tenant).PreviewAsync(input);
        await using var lockDb = database.CreateDbContext();
        await using var tx = await new TenantAdministrationGuard(lockDb).BeginChangeAsync(tenant.CompanyId);
        var entered = new ObserveCompanyLock();
        await using var waiting = new TestServiceScope(database, tenant.OperationalContext, entered);
        var pending = Service(waiting, tenant).ConfirmAsync(new() { Payload = input, PreviewToken = preview.PreviewToken! });
        await entered.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await lockDb.Users.Where(u => u.Id == tenant.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.SessionVersion, 2L));
        await tx.CommitAsync();
        var error = await Assert.ThrowsAsync<OperationalContextException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("SESSION_STALE", error.ErrorCode);
        Assert.Empty(await scope.DbContext.InitialDataBatches.ToListAsync());
    }

    [Fact]
    public async Task Readiness_is_read_only_and_does_not_initialize_settings_or_load_certificates()
    {
        var tenant = await TenantAsync("ready", 713);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var readiness = await Service(scope, tenant).GetReadinessAsync();
        Assert.Equal(tenant.CompanyId, readiness.CompanyId);
        Assert.False(readiness.Ready);
        Assert.Contains(readiness.Checks, c => c.Code == "fiscal-configuration" && !c.Ready);
        Assert.Empty(await scope.DbContext.CompanySriSettings.ToListAsync());
        Assert.Empty(await scope.DbContext.InitialDataBatches.ToListAsync());
    }

    [Fact]
    public async Task Operational_codes_addresses_and_used_identity_are_validated_without_renumbering()
    {
        var tenant = await TenantAsync("identity", 714);
        await using var scope = new TestServiceScope(database, tenant.OperationalContext);
        var db = scope.DbContext; var accessor = new StaticOperationalContextAccessor(tenant.OperationalContext);
        var guard = new TenantAdministrationGuard(db);
        var establishments = new EstablishmentsController(db, accessor, guard, null!);
        var points = new EmissionPointsController(db, accessor, guard, null!);
        var created = await establishments.Create(new() { Name = "Sucursal", Code = "017", Address = "Synthetic avenue 526" });
        var entity = Assert.IsType<EstablishmentDto>(Assert.IsType<CreatedAtActionResult>(created.Result).Value);
        Assert.Equal("017", entity.Code); Assert.Equal("Synthetic avenue 526", entity.Address);
        Assert.IsType<BadRequestObjectResult>((await establishments.Create(new() { Name = "Bad", Code = "000", Address = "N/A" })).Result);
        Assert.IsType<ConflictObjectResult>((await establishments.Create(new() { Name = "Duplicate", Code = "017", Address = "Synthetic address" })).Result);
        var pointResult = await points.Create(new() { EstablishmentId = entity.Id, Name = "Caja", Code = "009" });
        Assert.Equal("009", Assert.IsType<EmissionPointDto>(Assert.IsType<CreatedAtActionResult>(pointResult.Result).Value).Code);
        var fiscal = new FiscalSettingsService(db, accessor);
        var sequence = await fiscal.CreateDocumentSequenceAsync(new() { EstablishmentId = tenant.EstablishmentId,
            EmissionPointId = tenant.EmissionPointId, DocumentType = FiscalDocumentType.Invoice, NextNumber = 42, Reason = "Synthetic initial sequence" });
        Assert.IsType<ConflictObjectResult>(await points.Update(tenant.EmissionPointId, new() { Name = "Caja", Code = "008" }));
        Assert.IsType<ConflictObjectResult>(await establishments.Update(tenant.EstablishmentId,
            new() { Name = "Matriz", Code = "018", Address = "Synthetic address" }));
        var originalRuc = (await db.Companies.SingleAsync()).Ruc;
        var settings = new UpdateCompanyFiscalSettingsDto { Name = "Updated business", Ruc = "9000000009999", MatrixAddress = "Synthetic new address" };
        Assert.Equal("COMPANY_IDENTITY_ALREADY_USED", (await Assert.ThrowsAsync<InvalidOperationException>(() => fiscal.UpdateCompanyFiscalSettingsAsync(settings))).Message);
        settings.Ruc = originalRuc;
        Assert.Equal("Updated business", (await fiscal.UpdateCompanyFiscalSettingsAsync(settings)).Name);
        Assert.IsType<NoContentResult>(await establishments.Update(tenant.EstablishmentId,
            new() { Name = "Updated name", Code = "001", Address = "Synthetic new address" }));
        await using var verify = database.CreateDbContext();
        Assert.Equal(41, (await verify.DocumentSequences.SingleAsync(s => s.Id == sequence.Id)).CurrentNumber);
        Assert.Single(await verify.DocumentSequenceAudits.ToListAsync());
        Assert.Equal("001", (await verify.EmissionPoints.SingleAsync(p => p.Id == tenant.EmissionPointId)).Code);
    }

    private async Task<TestTenant> TenantAsync(string key, int seed, params decimal[] stocks)
    {
        var tenant = await TestDataBuilder.CreateTenantAsync(database, key, seed, stocks.Length == 0 ? [0m] : stocks);
        await using var db = database.CreateDbContext();
        var role = await db.Users.Where(u => u.Id == tenant.UserId).Select(u => u.Role).SingleAsync();
        role.Code = AppRoles.Admin;
        foreach (var code in new[] { AppPermissions.CatalogCategoriesRead, AppPermissions.CatalogCategoriesWrite,
            AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite, AppPermissions.CustomersRead,
            AppPermissions.CustomersWrite, AppPermissions.SuppliersRead, AppPermissions.SuppliersWrite,
            AppPermissions.InventoryRead, AppPermissions.InventoryWrite, AppPermissions.OpStructureRead,
            AppPermissions.FiscalSettingsRead, AppPermissions.AdminUsersRead })
        {
            var p = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code)
                ?? new Permission { Code = code, Description = code, IsActive = true, CreatedAt = DateTime.UtcNow };
            role.RolePermissions.Add(new RolePermission { Permission = p });
        }
        await db.SaveChangesAsync(); return tenant;
    }
    private InitialDataService Service(TestServiceScope scope, TestTenant tenant) => new(scope.DbContext,
        new StaticOperationalContextAccessor(tenant.OperationalContext), new TenantAdministrationGuard(scope.DbContext),
        scope.Inventory, new ProductCostService(scope.DbContext), protection);
    private static InitialDataPreviewRequest Payload(string kind, string csv) => new() { RequestId = Guid.NewGuid(), Kind = kind, Csv = csv };
    private static InventoryAdjustDto Count(InventoryStockDto snapshot, decimal target) => new()
    { RequestId = Guid.NewGuid(), ProductId = snapshot.ProductId, Quantity = target, ExpectedQuantity = snapshot.Quantity,
        ExpectedMovementWatermark = snapshot.MovementWatermark, ExpectedCompanyId = snapshot.CompanyId,
        ExpectedEstablishmentId = snapshot.EstablishmentId };
    private sealed class FailSecondMovement(int productId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<InventoryMovement>().Any(e => e.State == EntityState.Added && e.Entity.ProductId == productId))
                throw new InvalidOperationException("SYNTHETIC_SECOND_ROW_FAILURE");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
    private sealed class ObserveCompanyLock : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BackendPid { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("Companies") && command.CommandText.Contains("FOR UPDATE"))
            { BackendPid = ((NpgsqlConnection)command.Connection!).ProcessID; Started.TrySetResult(); }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
    private sealed class HoldCompanyLock : DbCommandInterceptor
    {
        public TaskCompletionSource Acquired { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BackendPid { get; private set; }
        private bool held;
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (!held && command.CommandText.Contains("Companies") && command.CommandText.Contains("FOR UPDATE"))
            {
                held = true; BackendPid = ((NpgsqlConnection)command.Connection!).ProcessID; Acquired.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }
    private async Task WaitUntilBlockedAsync(int ownerPid, int waitingPid)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var db = database.CreateDbContext();
        while (await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN {0} = ANY(pg_blocking_pids({1})) THEN 1 ELSE 0 END AS \"Value\"",
            ownerPid, waitingPid).SingleAsync(timeout.Token) != 1)
            await Task.Delay(20, timeout.Token);
    }
}
