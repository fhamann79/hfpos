using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class AutonomousElectronicIssuingTests(PostgresDatabaseFixture database) : IAsyncLifetime
{
    public Task InitializeAsync() => database.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private async Task<AutonomousIssuingFixture> Setup(string key = "autonomous", int seed = 901)
    {
        var fixture = new AutonomousIssuingFixture(database);
        await fixture.InitializeAsync(key, seed);
        return fixture;
    }

    [Fact]
    public async Task Legacy_default_off_never_backfills_and_settings_only_cannot_enable_but_can_revoke_with_audit()
    {
        var f = await Setup(); await f.SetDelegationAsync(false);
        var sale = await f.SellAsync();
        await using (var db = database.CreateDbContext())
        {
            Assert.Empty(await db.ElectronicIssuingJobs.ToListAsync());
            var user = await db.Users.SingleAsync(u => u.Id == f.Admin.UserId);
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(r => r.RoleId == user.RoleId
                && (r.Permission.Code == AppPermissions.SriDocumentsSign || r.Permission.Code == AppPermissions.SriDocumentsSubmit)).ToListAsync());
            await db.SaveChangesAsync();
        }
        Assert.Equal("FISCAL_PERMISSION_REQUIRED", (await Assert.ThrowsAsync<InvalidOperationException>(() => f.SetDelegationAsync(true))).Message);
        await using (var db = database.CreateDbContext())
        {
            Assert.False((await db.CompanySriSettings.SingleAsync()).AutomaticProcessingEnabled);
            Assert.Equal(2, await db.FiscalDelegationAudits.CountAsync());
            var roleId = await db.Users.Where(u => u.Id == f.Admin.UserId).Select(u => u.RoleId).SingleAsync();
            foreach (var p in await db.Permissions.Where(p => p.Code == AppPermissions.SriDocumentsSign || p.Code == AppPermissions.SriDocumentsSubmit).ToListAsync())
                db.RolePermissions.Add(new Pos.Backend.Api.Core.Entities.RolePermission { RoleId = roleId, PermissionId = p.Id });
            await db.SaveChangesAsync();
        }
        await f.SetDelegationAsync(true);
        await using (var db = database.CreateDbContext())
        {
            Assert.Empty(await db.ElectronicIssuingJobs.ToListAsync()); // Explicit enabling is not a legacy backfill.
            var roleId = await db.Users.Where(u => u.Id == f.Admin.UserId).Select(u => u.RoleId).SingleAsync();
            db.RolePermissions.RemoveRange(await db.RolePermissions.Where(r => r.RoleId == roleId
                && (r.Permission.Code == AppPermissions.SriDocumentsSign || r.Permission.Code == AppPermissions.SriDocumentsSubmit)).ToListAsync());
            await db.SaveChangesAsync();
        }
        await f.SetDelegationAsync(false);
        await using var verify = database.CreateDbContext();
        Assert.Equal(new long[] { 1, 2, 3, 4 }, await verify.FiscalDelegationAudits.OrderBy(a => a.Revision).Select(a => a.Revision).ToArrayAsync());
        Assert.All(await verify.FiscalDelegationAudits.ToListAsync(), a => Assert.Equal(f.Admin.UserId, a.UserId));
        Assert.All(await verify.FiscalDelegations.ToListAsync(), d => Assert.NotNull(d.DisabledAt));
        Assert.Equal(sale.AccessKey, (await verify.Sales.SingleAsync()).AccessKey);
    }

    [Fact]
    public async Task Final_expired_lease_release_rolls_back_document_attempt_and_job_mutations_together()
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        var claim = await f.ClaimAsync(); var expiry = new ExpireFinalRelease();
        await using var worker = f.Worker(expiry);
        await worker.Processor.ProcessAsync(claim, CancellationToken.None);
        Assert.True(expiry.Triggered);
        await using var verify = database.CreateDbContext();
        Assert.Null((await verify.Sales.SingleAsync()).SriSubmittedAt);
        var job = await verify.ElectronicIssuingJobs.SingleAsync();
        Assert.Equal(ElectronicIssuingJobState.Processing, job.State);
        Assert.Equal(ElectronicIssuingPhase.ReceptionInFlight, job.Phase);
        Assert.Equal(claim.LeaseToken, job.LeaseToken);
        Assert.Empty(await verify.SriSubmissionAttempts.ToListAsync());
    }

    private sealed class ExpireFinalRelease : DbCommandInterceptor
    {
        public bool Triggered { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (command.CommandText.Contains("SET \"LeaseToken\" = NULL, \"LeaseExpiresAt\" = NULL", StringComparison.Ordinal))
            {
                Triggered = true;
                await using var expire = command.Connection!.CreateCommand();
                expire.Transaction = command.Transaction;
                expire.CommandText = "UPDATE \"ElectronicIssuingJobs\" SET \"LeaseExpiresAt\" = clock_timestamp() - interval '1 second'";
                await expire.ExecuteNonQueryAsync(ct); // Real SQL expiry immediately before the final conditional write.
            }
            return result;
        }
    }

    [Fact]
    public async Task Cashier_only_sales_permission_enqueues_atomically_and_worker_survives_seller_session_revocation_without_http()
    {
        var f = await Setup(); var request = f.Request();
        var sale = await f.SellAsync(request);
        Assert.Equal(sale.Id, (await f.SellAsync(request)).Id);
        await using (var db = database.CreateDbContext())
        {
            var cashier = await db.Users.SingleAsync(u => u.Id == f.Tenant.UserId);
            var permissions = await db.RolePermissions.Where(p => p.RoleId == cashier.RoleId).Select(p => p.Permission.Code).ToListAsync();
            Assert.Equal([AppPermissions.PosSalesCreate], permissions);
            cashier.SessionVersion++; cashier.IsActive = false;
            await db.SaveChangesAsync();
            Assert.Single(await db.ElectronicIssuingJobs.ToListAsync());
            Assert.Single(await db.InventoryMovements.ToListAsync());
            Assert.Single(await db.DocumentSequences.ToListAsync());
        }
        await f.ProcessNextAsync(); // Real XAdES signing and generated ephemeral certificate.
        await f.ProcessNextAsync();
        await f.DueAsync(sale.Id);
        await f.ProcessNextAsync(); // Fresh DbContext/scope after each stage, no browser identity.
        await using var verify = database.CreateDbContext();
        var authorized = await verify.Sales.SingleAsync();
        Assert.Equal(SaleDocumentStatus.Authorized, authorized.DocumentStatus);
        Assert.Equal(sale.AccessKey, f.Transport.LastKey);
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(1, f.Transport.Queries);
        Assert.Equal(ElectronicIssuingJobState.Authorized, (await verify.ElectronicIssuingJobs.SingleAsync()).State);
        Assert.All(await verify.SriSubmissionAttempts.ToListAsync(), a =>
        { Assert.Equal(f.Admin.UserId, a.CreatedByUserId); Assert.NotNull(a.FiscalDelegationId); });
        var cashierRole = await verify.Users.Where(u => u.Id == f.Tenant.UserId).Select(u => u.RoleId).SingleAsync();
        Assert.Equal([AppPermissions.PosSalesCreate], await verify.RolePermissions.Where(p => p.RoleId == cashierRole)
            .Select(p => p.Permission.Code).ToListAsync());
    }

    [Fact]
    public async Task Sale_outbox_stock_failure_rolls_back_all_effects_and_replay_has_one_job()
    {
        var f = await Setup(); var request = f.Request(); request.Items[0].Quantity = 51; request.CashReceived = 1000;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.SellAsync(request));
        await using (var db = database.CreateDbContext())
        {
            Assert.Empty(await db.Sales.ToListAsync()); Assert.Empty(await db.ElectronicIssuingJobs.ToListAsync());
            Assert.Empty(await db.DocumentSequences.ToListAsync()); Assert.Empty(await db.InventoryMovements.ToListAsync());
        }
        request.Items[0].Quantity = 1;
        var sale = await f.SellAsync(request); Assert.Equal(sale.Id, (await f.SellAsync(request)).Id);
        await using var verify = database.CreateDbContext(); Assert.Single(await verify.ElectronicIssuingJobs.ToListAsync());
    }

    [Fact]
    public async Task Claim_skips_a_proven_locked_job_and_two_claimers_never_own_same_generation()
    {
        var f = await Setup(); var first = await f.SellAsync(); var second = await f.SellAsync();
        await using var owner = database.CreateDbContext(); await using var tx = await owner.Database.BeginTransactionAsync();
        var locked = await owner.ElectronicIssuingJobs.FromSqlInterpolated($"SELECT * FROM \"ElectronicIssuingJobs\" WHERE \"SaleId\" = {first.Id} FOR UPDATE").SingleAsync();
        await using var claimant = f.Worker();
        var skipped = Assert.Single(await claimant.Coordinator.ClaimBatchAsync(2, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(second.Id, skipped.SaleId); Assert.NotEqual(locked.Id, skipped.Id);
        Console.WriteLine($"SKIP LOCKED proof: held job={locked.Id}, holder pid={((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID}, returned job={skipped.Id}");
        await tx.CommitAsync();
        var other = await f.ClaimAsync(); Assert.Equal(first.Id, other.SaleId); Assert.NotEqual(other.LeaseToken, skipped.LeaseToken);
        await using var third = f.Worker(); Assert.Empty(await third.Coordinator.ClaimBatchAsync(2, CancellationToken.None));
    }

    [Theory]
    [InlineData("ready-sign", "FISCAL_RECEPTION_NOT_ADMITTED")]
    [InlineData("ready-submit", "FISCAL_RECEPTION_NOT_ADMITTED")]
    [InlineData("rejected", "FISCAL_DOCUMENT_TERMINAL")]
    public async Task Manual_query_cannot_skip_reception_or_reopen_definitive_rejection(string stage, string error)
    {
        var f = await Setup(); var sale = await f.SellAsync();
        if (stage != "ready-sign") await f.ProcessNextAsync();
        if (stage == "rejected")
        {
            f.Transport.Reception = _ => Task.FromResult(new Core.Models.SriReceptionResponse { Estado = "DEVUELTA" });
            await f.ProcessNextAsync();
        }
        await using var before = database.CreateDbContext();
        var original = await before.ElectronicIssuingJobs.AsNoTracking().SingleAsync();
        var attempts = await before.SriSubmissionAttempts.CountAsync();
        f.Transport.Authorization = _ => Task.FromResult(new Core.Models.SriAuthorizationResponse { Estado = "NOT_FOUND" });
        await using (var manual = new FiscalWorkerTestScope(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
            new StaticOperationalContextAccessor(f.Admin)))
            Assert.Equal(error, (await Assert.ThrowsAsync<InvalidOperationException>(() => manual.Submission.CheckAuthorizationAsync(sale.Id))).Message);
        using (var factory = new FiscalApiFactory(database, f.Transport))
        using (var client = factory.CreateClient())
        {
            var admin = await before.Users.SingleAsync(u => u.Id == f.Admin.UserId);
            var jwt = new JwtService(Options.Create(new JwtOptions
            { Key = FiscalApiFactory.Key, Issuer = "hfpos-528", Audience = "hfpos-528", ExpiresMinutes = 10 }), before).GenerateToken(admin);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await client.PostAsync($"/api/Sales/{sale.Id}/sri/check-authorization", null);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(error, (await response.Content.ReadFromJsonAsync<Dictionary<string, string>>())!["error"]);
        }
        Assert.Equal(0, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        var unchanged = await verify.ElectronicIssuingJobs.SingleAsync();
        Assert.Equal(original.Phase, unchanged.Phase); Assert.Equal(original.State, unchanged.State);
        Assert.Equal(original.Fence, unchanged.Fence); Assert.Equal(original.AttemptCount, unchanged.AttemptCount);
        Assert.Equal(original.LeaseToken, unchanged.LeaseToken); Assert.Equal(original.LeaseExpiresAt, unchanged.LeaseExpiresAt);
        Assert.Equal(original.NextAttemptAt, unchanged.NextAttemptAt); Assert.Equal(original.UpdatedAt, unchanged.UpdatedAt);
        Assert.Equal(original.ReceptionStartedAt, unchanged.ReceptionStartedAt);
        Assert.Equal(attempts, await verify.SriSubmissionAttempts.CountAsync());
        if (stage == "rejected")
        {
            await using var scope = new TestServiceScope(database, f.Tenant.OperationalContext);
            Assert.Equal(SaleStatus.Voided, (await f.Sales(scope).VoidAsync(sale.Id, new() { Reason = "Synthetic terminal query guard" })).Status);
            Assert.Equal(50m, await TestDataBuilder.GetStockAsync(scope.DbContext, f.Tenant, f.Tenant.Products[0].Id));
            Assert.Equal(0m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Legacy_known_reception_can_be_queried_but_definitive_rejection_stays_terminal(bool rejected)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        if (rejected) f.Transport.Reception = _ => Task.FromResult(new Core.Models.SriReceptionResponse { Estado = "DEVUELTA" });
        await f.ProcessNextAsync();
        await using (var db = database.CreateDbContext())
        { db.ElectronicIssuingJobs.Remove(await db.ElectronicIssuingJobs.SingleAsync()); await db.SaveChangesAsync(); }
        f.Transport.Authorization = _ => Task.FromResult(new Core.Models.SriAuthorizationResponse { Estado = "NOT_FOUND" });
        await using (var manual = new FiscalWorkerTestScope(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
            new StaticOperationalContextAccessor(f.Admin)))
            Assert.Equal(rejected ? "FISCAL_DOCUMENT_TERMINAL" : "SRI_AUTHORIZATION_PENDING",
                (await Assert.ThrowsAsync<InvalidOperationException>(() => manual.Submission.CheckAuthorizationAsync(sale.Id))).Message);
        Assert.Equal(rejected ? 0 : 1, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        if (rejected) Assert.Empty(await verify.ElectronicIssuingJobs.ToListAsync());
        else
        {
            var job = await verify.ElectronicIssuingJobs.SingleAsync();
            Assert.Equal(ElectronicIssuingJobState.WaitingAuthorization, job.State);
            Assert.False(job.AutomaticRequested); Assert.Equal(sale.AccessKey, job.AccessKey);
        }
    }

    [Fact]
    public async Task Hosted_worker_claims_second_job_only_after_long_first_call_with_its_own_lease_and_attempt_budget()
    {
        var f = await Setup(); var first = await f.SellAsync(); await f.ProcessNextAsync();
        // Hide the first ready reception briefly so the second can be signed before starting the actual hosted worker.
        await using (var db = database.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ElectronicIssuingJobs\" SET \"NextAttemptAt\" = clock_timestamp() + interval '1 hour' WHERE \"SaleId\" = {first.Id}");
        var second = await f.SellAsync(); await f.ProcessNextAsync();
        await using (var db = database.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ElectronicIssuingJobs\" SET \"NextAttemptAt\" = clock_timestamp() - interval '1 hour' WHERE \"SaleId\" = {first.Id}");
        var enteredFirst = new AsyncTestSignal(); var enteredSecond = new AsyncTestSignal();
        var releaseFirst = new AsyncTestSignal(); var releaseSecond = new AsyncTestSignal();
        f.Transport.Reception = async ct =>
        {
            if (f.Transport.Submissions == 1) { enteredFirst.Set(); await releaseFirst.WaitAsync(ct); }
            else { enteredSecond.Set(); await releaseSecond.WaitAsync(ct); }
            return new() { Estado = "RECIBIDA" };
        };
        var services = new ServiceCollection();
        services.AddScoped(_ => f.Worker());
        services.AddScoped(sp => sp.GetRequiredService<FiscalWorkerTestScope>().Coordinator);
        services.AddScoped(sp => sp.GetRequiredService<FiscalWorkerTestScope>().Processor);
        await using var provider = services.BuildServiceProvider();
        using var worker = new ElectronicIssuingWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<ElectronicIssuingWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await enteredFirst.WaitAsync();
            await using (var db = database.CreateDbContext())
            {
                var waiting = await db.ElectronicIssuingJobs.SingleAsync(j => j.SaleId == second.Id);
                Assert.Null(waiting.LeaseToken); Assert.Null(waiting.LeaseExpiresAt);
                Assert.Equal(1, waiting.AttemptCount); // Signing only; not preclaimed while first reception is held.
                // Controlled real SQL expiry represents a first call consuming its entire lease, without a 120s sleep.
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"ElectronicIssuingJobs\" SET \"LeaseExpiresAt\" = clock_timestamp() - interval '1 second', \"NextAttemptAt\" = clock_timestamp() + interval '1 hour' WHERE \"SaleId\" = {first.Id}");
            }
            releaseFirst.Set(); await enteredSecond.WaitAsync();
            await using var verify = database.CreateDbContext();
            var ownLease = await verify.ElectronicIssuingJobs.SingleAsync(j => j.SaleId == second.Id);
            Assert.NotNull(ownLease.LeaseToken); Assert.Equal(2, ownLease.AttemptCount);
            Assert.Equal(ElectronicIssuingPhase.ReceptionInFlight, ownLease.Phase);
            Assert.True(await verify.Database.SqlQuery<bool>($"SELECT \"LeaseExpiresAt\" > clock_timestamp() + interval '100 seconds' AS \"Value\" FROM \"ElectronicIssuingJobs\" WHERE \"SaleId\" = {second.Id}").SingleAsync());
        }
        finally
        {
            releaseFirst.Set(); releaseSecond.Set();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(shutdown.Token);
        }
        Assert.Equal(2, f.Transport.Submissions); Assert.Equal(0, f.Transport.Queries);
        await using var final = database.CreateDbContext();
        var untouched = await final.ElectronicIssuingJobs.SingleAsync(j => j.SaleId == first.Id);
        Assert.Equal(ElectronicIssuingPhase.ReceptionInFlight, untouched.Phase); // Expired result stayed fenced.
        Assert.Null((await final.Sales.SingleAsync(s => s.Id == first.Id)).SriSubmittedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stale_reception_result_or_error_cannot_overwrite_new_owner_authorization_and_pos_is_not_locked(bool fail)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        var entered = new AsyncTestSignal(); var release = new AsyncTestSignal();
        f.Transport.Reception = async ct =>
        {
            entered.Set(); await release.WaitAsync(ct);
            if (fail) throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED");
            return new() { Estado = "RECIBIDA" };
        };
        var a = await f.ClaimAsync(); await using var scopeA = f.Worker();
        var pending = scopeA.Processor.ProcessAsync(a, CancellationToken.None);
        try
        {
            await entered.WaitAsync();
            var ticket = f.Request(); ticket.DocumentType = SaleDocumentType.Ticket;
            await f.SellAsync(ticket).WaitAsync(TimeSpan.FromSeconds(5)); // Actual POS transaction progresses while external request is held.
            await f.DueAsync(sale.Id, expire: true);
            var b = await f.ClaimAsync(); Assert.Equal(ElectronicIssuingPhase.UnknownReception, b.Phase);
            Assert.True(b.Fence > a.Fence);
            await using var scopeB = f.Worker(); await scopeB.Processor.ProcessAsync(b, CancellationToken.None);
            Assert.False(await scopeA.Coordinator.HeartbeatAsync(a, CancellationToken.None));
        }
        finally { release.Set(); await pending.WaitAsync(TimeSpan.FromSeconds(10)); }
        await using var verify = database.CreateDbContext();
        Assert.Equal(SaleDocumentStatus.Authorized, (await verify.Sales.SingleAsync(s => s.Id == sale.Id)).DocumentStatus);
        Assert.Equal(ElectronicIssuingJobState.Authorized, (await verify.ElectronicIssuingJobs.SingleAsync()).State);
        Assert.Single(await verify.SriSubmissionAttempts.ToListAsync());
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(1, f.Transport.Queries);
    }

    [Fact]
    public async Task Ambiguous_remote_accept_and_eventual_not_found_are_query_only_never_resubmitted_even_manually()
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        f.Transport.Reception = _ => throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED");
        await f.ProcessNextAsync();
        f.Transport.Authorization = _ => Task.FromResult(new Core.Models.SriAuthorizationResponse { Estado = "NOT_FOUND" });
        await f.DueAsync(sale.Id); await f.ProcessNextAsync();
        await using (var manual = new FiscalWorkerTestScope(database.CreateDbContext(), f.Protection, f.Transport, f.Options,
            new StaticOperationalContextAccessor(f.Admin)))
            Assert.Equal("FISCAL_RECEPTION_ALREADY_ADMITTED", (await Assert.ThrowsAsync<InvalidOperationException>(() => manual.Submission.SubmitSignedInvoiceAsync(sale.Id))).Message);
        f.Transport.Authorization = null;
        await f.DueAsync(sale.Id); await f.ProcessNextAsync();
        Assert.Equal(1, f.Transport.Submissions); Assert.Equal(2, f.Transport.Queries);
        Assert.Equal(sale.AccessKey, f.Transport.LastKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Crash_reserved_or_admitted_recovers_without_losing_identity(bool admitted)
    {
        var f = await Setup(); var sale = await f.SellAsync();
        if (admitted) await f.ProcessNextAsync();
        var claim = await f.ClaimAsync();
        if (admitted)
        {
            var entered = new AsyncTestSignal(); using var cancel = new CancellationTokenSource();
            f.Transport.Reception = async ct => { entered.Set(); await Task.Delay(Timeout.Infinite, ct); return new(); };
            await using var crashed = f.Worker();
            var run = crashed.Processor.ProcessAsync(claim, cancel.Token);
            await entered.WaitAsync(); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
        await f.DueAsync(sale.Id, expire: true);
        f.Transport.Reception = null;
        await f.ProcessNextAsync();
        Assert.Equal(admitted ? 1 : 0, f.Transport.Submissions);
        Assert.Equal(admitted ? 1 : 0, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        Assert.Single(await verify.ElectronicIssuingJobs.ToListAsync()); Assert.Equal(sale.AccessKey, (await verify.Sales.SingleAsync()).AccessKey);
    }

    [Theory]
    [InlineData(false, "grant")]
    [InlineData(true, "grant")]
    [InlineData(false, "settings")]
    [InlineData(true, "settings")]
    [InlineData(false, "certificate")]
    [InlineData(true, "certificate")]
    [InlineData(false, "suspension")]
    [InlineData(true, "suspension")]
    public async Task Delegation_revocation_and_external_admission_have_proven_serial_order(bool admissionFirst, string barrier)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        Core.Models.PlatformContext? platform = null;
        if (barrier == "suspension")
        {
            await using var db = database.CreateDbContext();
            var actor = new Core.Entities.PlatformUser { Username = "synthetic-platform", Email = "platform@hfpos.test",
                PasswordHash = "synthetic-only", CreatedAt = DateTime.UtcNow };
            db.PlatformUsers.Add(actor); await db.SaveChangesAsync();
            platform = new(actor.Id, actor.Username, actor.Email, actor.SessionVersion);
        }
        async Task Revoke(PosDbContext db)
        {
            if (barrier == "grant") await f.SetDelegationAsync(false, db);
            if (barrier == "settings") await new FiscalSettingsService(db, new StaticOperationalContextAccessor(f.Admin))
                .UpdateCompanySriSettingsAsync(new() { Environment = 1, EmissionType = 1, IsEnabled = false, AutomaticProcessingEnabled = true });
            if (barrier == "certificate") await new SriCertificateService(db, new StaticOperationalContextAccessor(f.Admin),
                f.Protection, NullLogger<SriCertificateService>.Instance).DeactivateCertificateAsync();
            if (barrier == "suspension") await new PlatformTenantService(db, new FixedPlatformContext(platform!),
                new BusinessClockService(), new TenantAdministrationGuard(db))
                .SetActiveAsync(f.Tenant.CompanyId, false, new("Synthetic fiscal suspension"));
        }
        var claim = await f.ClaimAsync(); var companyHeld = new AsyncTestSignal(); var releaseSql = new AsyncTestSignal();
        var externalHeld = new AsyncTestSignal(); var releaseExternal = new AsyncTestSignal();
        f.Transport.Reception = async ct => { externalHeld.Set(); await releaseExternal.WaitAsync(ct); return new() { Estado = "RECIBIDA" }; };
        await using var winner = database.CreateDbContext(
            SqlCommandGateInterceptor.SignalAfter(SqlCommandMatchers.CompanyLock, companyHeld),
            SqlCommandGateInterceptor.WaitBefore(sql => admissionFirst ? sql.Contains("FROM \"Sales\"") && sql.Contains("FOR UPDATE")
                : sql.Contains(barrier == "certificate" ? "FROM \"CompanySriCertificates\""
                    : barrier == "suspension" ? "FROM \"PlatformUsers\"" : "FROM \"CompanySriSettings\""), releaseSql));
        await using var loser = database.CreateDbContext();
        await using var worker = new FiscalWorkerTestScope(admissionFirst ? winner : loser, f.Protection, f.Transport, f.Options);
        Task? first = null; Task? second = null;
        try
        {
            first = admissionFirst ? worker.Processor.ProcessAsync(claim, CancellationToken.None) : Revoke(winner);
            await companyHeld.WaitAsync();
            second = admissionFirst ? Revoke(loser) : worker.Processor.ProcessAsync(claim, CancellationToken.None);
            await AssertBlockedAsync(loser, winner);
            releaseSql.Set();
            if (admissionFirst)
            {
                await externalHeld.WaitAsync(); await second.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            releaseSql.Set(); releaseExternal.Set();
            await Task.WhenAll(new[] { first, second }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.Equal(admissionFirst ? 1 : 0, f.Transport.Submissions);
        if (admissionFirst) { await f.DueAsync(sale.Id); await f.ProcessNextAsync(); }
        Assert.Equal(0, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        Assert.Equal(ElectronicIssuingJobState.ManualAttention, (await verify.ElectronicIssuingJobs.SingleAsync()).State);
        Assert.Equal(barrier == "certificate" ? 1 : 2, await verify.FiscalDelegationAudits.CountAsync());
        if (barrier == "suspension")
        {
            await new PlatformTenantService(verify, new FixedPlatformContext(platform!), new BusinessClockService(), new TenantAdministrationGuard(verify))
                .SetActiveAsync(f.Tenant.CompanyId, true, new("Synthetic reactivation"));
            Assert.False((await verify.CompanySriSettings.SingleAsync()).AutomaticProcessingEnabled);
            Assert.NotNull((await verify.FiscalDelegations.SingleAsync()).DisabledAt);
        }
    }

    private sealed class FixedPlatformContext(Core.Models.PlatformContext actor) : IPlatformContextAccessor
    {
        public Task<Core.Models.PlatformContext> GetRequiredContextAsync() => Task.FromResult(actor);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("settings")]
    [InlineData("certificate")]
    [InlineData("production")]
    [InlineData("context")]
    public async Task Current_policy_blocks_new_reception_without_transport(string barrier)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        await using (var db = database.CreateDbContext())
        {
            if (barrier == "tenant") (await db.Companies.SingleAsync()).IsActive = false;
            if (barrier == "settings") (await db.CompanySriSettings.SingleAsync()).IsEnabled = false;
            if (barrier == "certificate") (await db.CompanySriCertificates.SingleAsync()).IsActive = false;
            if (barrier == "production")
            {
                (await db.CompanySriSettings.SingleAsync()).Environment = 2;
                (await db.Sales.SingleAsync()).SriEnvironment = 2;
                (await db.ElectronicIssuingJobs.SingleAsync()).Environment = 2;
            }
            if (barrier == "context") (await db.Sales.SingleAsync()).AccessKey = new string('9', 49);
            await db.SaveChangesAsync();
        }
        await f.ProcessNextAsync(); Assert.Equal(0, f.Transport.Submissions); Assert.Equal(0, f.Transport.Queries);
        await using var verify = database.CreateDbContext();
        Assert.Equal(ElectronicIssuingJobState.ManualAttention, (await verify.ElectronicIssuingJobs.SingleAsync()).State);
    }

    [Fact]
    public async Task Pending_backoff_is_bounded_rejection_is_terminal_and_disabling_preserves_documents()
    {
        var f = await Setup(); var sale = await f.SellAsync();
        f.Transport.Authorization = _ => Task.FromResult(new Core.Models.SriAuthorizationResponse { Estado = "NOT_FOUND" });
        for (var i = 0; i < ElectronicIssuingCoordinator.MaximumAttempts; i++) { await f.DueAsync(sale.Id); await f.ProcessNextAsync(); }
        await using (var scope = f.Worker()) Assert.Empty(await scope.Coordinator.ClaimBatchAsync(2, CancellationToken.None));
        await using (var verify = database.CreateDbContext())
        {
            var job = await verify.ElectronicIssuingJobs.SingleAsync();
            Assert.Equal(ElectronicIssuingJobState.ManualAttention, job.State); Assert.Equal(12, job.AttemptCount);
            Assert.True(job.NextAttemptAt > job.UpdatedAt);
        }
        var rejected = await f.SellAsync(); await f.ProcessNextAsync();
        f.Transport.Reception = _ => Task.FromResult(new Core.Models.SriReceptionResponse { Estado = "DEVUELTA" });
        await f.ProcessNextAsync(); await f.DueAsync(rejected.Id);
        await using (var scope = f.Worker()) Assert.Empty(await scope.Coordinator.ClaimBatchAsync(2, CancellationToken.None));
        await f.SetDelegationAsync(false);
        await using var db = database.CreateDbContext(); Assert.Equal(2, await db.Sales.CountAsync()); Assert.Equal(2, await db.ElectronicIssuingJobs.CountAsync());
    }

    [Fact]
    public async Task Cashier_actual_jwt_cannot_manually_sign_submit_query_or_change_company_delegation()
    {
        var f = await Setup(); var sale = await f.SellAsync();
        using var factory = new FiscalApiFactory(database);
        using var client = factory.CreateClient();
        await using var db = database.CreateDbContext();
        var cashier = await db.Users.SingleAsync(u => u.Id == f.Tenant.UserId);
        var jwt = new JwtService(Options.Create(new JwtOptions { Key = FiscalApiFactory.Key, Issuer = "hfpos-528", Audience = "hfpos-528", ExpiresMinutes = 10 }), db).GenerateToken(cashier);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        foreach (var action in new[] { "sign", "submit", "check-authorization" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/Sales/{sale.Id}/sri/{action}", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsync("/api/FiscalSettings/sri", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(0, f.Transport.Submissions); Assert.Equal(0, f.Transport.Queries);
        Assert.DoesNotContain(AppPermissions.SriDocumentsSign, new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(jwt).Claims.Select(c => c.Value));
        Assert.DoesNotContain(AppPermissions.SriDocumentsSubmit, await db.RolePermissions.Where(p => p.RoleId == cashier.RoleId).Select(p => p.Permission.Code).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Definitive_reception_or_authorization_rejection_preserves_void_stock_and_cash_semantics(bool authorization)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        if (!authorization) f.Transport.Reception = _ => Task.FromResult(new Core.Models.SriReceptionResponse { Estado = "DEVUELTA" });
        await f.ProcessNextAsync();
        if (authorization)
        {
            f.Transport.Authorization = _ => Task.FromResult(new Core.Models.SriAuthorizationResponse { Estado = "NO AUTORIZADO" });
            await f.DueAsync(sale.Id); await f.ProcessNextAsync();
        }
        await using var scope = new TestServiceScope(database, f.Tenant.OperationalContext);
        var result = await f.Sales(scope).VoidAsync(sale.Id, new() { Reason = "Synthetic definitive rejection" });
        Assert.Equal(SaleStatus.Voided, result.Status);
        Assert.Equal(50m, await TestDataBuilder.GetStockAsync(scope.DbContext, f.Tenant, f.Tenant.Products[0].Id));
        Assert.Equal(0m, (await scope.CashSessions.GetCurrentAsync())!.ExpectedCashAmount);
        Assert.Equal(ElectronicIssuingPhase.Completed, (await scope.DbContext.ElectronicIssuingJobs.SingleAsync()).Phase);
        await using var worker = f.Worker(); Assert.Empty(await worker.Coordinator.ClaimBatchAsync(2, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Void_and_reception_admission_serialize_in_both_orders(bool admissionFirst)
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync(); var claim = await f.ClaimAsync();
        var held = new AsyncTestSignal(); var releaseSql = new AsyncTestSignal();
        var externalHeld = new AsyncTestSignal(); var releaseExternal = new AsyncTestSignal();
        f.Transport.Reception = async ct => { externalHeld.Set(); await releaseExternal.WaitAsync(ct); return new() { Estado = "RECIBIDA" }; };
        var gates = new Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[]
        {
            SqlCommandGateInterceptor.SignalAfter(sql => sql.Contains("FROM \"Sales\"") && sql.Contains("FOR UPDATE"), held),
            SqlCommandGateInterceptor.WaitBefore(sql => sql.Contains("FROM \"ElectronicIssuingJobs\"") && sql.Contains("FOR UPDATE"), releaseSql)
        };
        await using var voidScope = new TestServiceScope(database, f.Tenant.OperationalContext, admissionFirst ? [] : gates);
        await using var workScope = f.Worker(admissionFirst ? gates : []);
        Task? first = null; Task? second = null;
        Exception? voidError = null;
        async Task Void()
        {
            try { await f.Sales(voidScope).VoidAsync(sale.Id, new() { Reason = "Synthetic void/admission order" }); }
            catch (InvalidOperationException ex) { voidError = ex; }
        }
        try
        {
            first = admissionFirst ? workScope.Processor.ProcessAsync(claim, CancellationToken.None) : Void();
            await held.WaitAsync();
            second = admissionFirst ? Void() : workScope.Processor.ProcessAsync(claim, CancellationToken.None);
            await AssertBlockedAsync(admissionFirst ? voidScope.DbContext : workScope.Db, admissionFirst ? workScope.Db : voidScope.DbContext);
            releaseSql.Set();
            if (admissionFirst) await externalHeld.WaitAsync();
        }
        finally
        {
            releaseSql.Set(); releaseExternal.Set();
            await Task.WhenAll(new[] { first, second }.OfType<Task>()).WaitAsync(TimeSpan.FromSeconds(30));
        }
        Assert.Equal(admissionFirst ? 1 : 0, f.Transport.Submissions);
        Assert.Equal(admissionFirst ? "SALE_INVOICE_SRI_IN_PROGRESS_NOT_VOIDABLE" : null, voidError?.Message);
        await using var db = database.CreateDbContext();
        Assert.Equal(admissionFirst ? SaleStatus.Completed : SaleStatus.Voided, (await db.Sales.SingleAsync()).Status);
    }

    [Fact]
    public async Task Historical_rejected_flag_does_not_make_an_unknown_reception_voidable()
    {
        var f = await Setup(); var sale = await f.SellAsync(); await f.ProcessNextAsync();
        f.Transport.Reception = _ => throw new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED");
        await f.ProcessNextAsync();
        await using (var db = database.CreateDbContext())
        { (await db.Sales.SingleAsync()).DocumentStatus = SaleDocumentStatus.Rejected; await db.SaveChangesAsync(); }
        await using var scope = new TestServiceScope(database, f.Tenant.OperationalContext);
        Assert.Equal("SALE_INVOICE_SRI_IN_PROGRESS_NOT_VOIDABLE", (await Assert.ThrowsAsync<InvalidOperationException>(() =>
            f.Sales(scope).VoidAsync(sale.Id, new() { Reason = "Synthetic stale state" }))).Message);
    }

    private async Task AssertBlockedAsync(PosDbContext waiter, PosDbContext owner)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var observer = new NpgsqlConnection(database.ConnectionString); await observer.OpenAsync(timeout.Token);
        var ownerId = ((NpgsqlConnection)owner.Database.GetDbConnection()).ProcessID;
        while (true)
        {
            if (waiter.Database.GetDbConnection() is NpgsqlConnection { State: System.Data.ConnectionState.Open } connection)
            {
                await using var command = new NpgsqlCommand("SELECT @owner = ANY(pg_blocking_pids(@waiter))", observer);
                command.Parameters.AddWithValue("owner", ownerId); command.Parameters.AddWithValue("waiter", connection.ProcessID);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!)
                { Console.WriteLine($"Company order proof: waiter={connection.ProcessID}, blocker={ownerId}"); return; }
            }
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class FiscalApiFactory(PostgresDatabaseFixture database, ISriWebServiceClient? transport = null) : WebApplicationFactory<Program>
    {
        public const string Key = "hfpos-528-synthetic-test-only-jwt-key-long-enough-for-hmac";
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = database.ConnectionString, ["SeedDemoData"] = "false",
                ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "hfpos-528", ["Jwt:Audience"] = "hfpos-528"
            }));
            builder.ConfigureServices(services =>
            {
                if (transport is not null) services.AddSingleton(transport);
                var worker = services.Single(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(ElectronicIssuingWorker));
                services.Remove(worker); // Deterministic HTTP boundary checks; actual processor exercised above.
            });
        }
    }
}
