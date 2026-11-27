using System.Data.Common;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.Tests.Infrastructure;

namespace Pos.Backend.Api.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class ExtraordinaryFiscalRecoveryTests(PostgresDatabaseFixture db) : IAsyncLifetime
{
    private static readonly SriReceptionRecoveryRequest Grounds = new(null, "Recuperar factura", true);
    private const string NoData = "  NO existen   datos para los parámetros \n ingresados ";

    public Task InitializeAsync() => db.ResetDataAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(AutonomousIssuingFixture Fixture, Sale Sale)> SeedAsync()
    {
        var fixture = new AutonomousIssuingFixture(db);
        await fixture.InitializeAsync("fiscal-183", 983);
        var sale = await fixture.SellAsync();
        await fixture.ProcessNextAsync();
        fixture.Transport.Reception = _ => Task.FromException<SriReceptionResponse>(
            new InvalidOperationException("SRI_RECEPTION_COMMUNICATION_FAILED"));
        await fixture.ProcessNextAsync();
        await using var read = db.CreateDbContext();
        return (fixture, await read.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.Id));
    }

    [Fact]
    public async Task Original_bytes_two_durable_probes_and_one_admission_precede_post_and_block_worker_and_double_click()
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original);
        var fence = new HoldAfterDispatchVerification();
        await using var scope = Scope(fixture, soap, interceptors: [fence]);
        await using var duplicate = Scope(fixture, soap);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        soap.BeforeReply = async (operation, ct) =>
        {
            if (operation != "reception")
            {
                Assert.Null(scope.Db.Database.CurrentTransaction);
                return;
            }
            Assert.NotNull(scope.Db.Database.CurrentTransaction);
            await using var read = db.CreateDbContext();
            var audit = await read.SriSubmissionAttempts.OrderBy(a => a.Id).ToListAsync(ct);
            Assert.Equal(new[] { SriSubmissionAttemptType.Reception, SriSubmissionAttemptType.Authorization,
                SriSubmissionAttemptType.Validity, SriSubmissionAttemptType.ExtraordinaryReception }, audit.Select(a => a.AttemptType));
            Assert.Equal(FiscalRecoveryEvidence.ValidityNoRecords, audit[2].ErrorCode);
            Assert.Equal(FiscalRecoveryEvidence.OutcomeUnknown, audit[3].ErrorCode);
            Assert.NotNull(audit[3].RecoveryEvidenceId);
            Assert.All(audit.Skip(1), a => Assert.Equal(audit[3].RecoveryEvidenceId, a.RecoveryEvidenceId));
            Assert.NotNull((await read.ElectronicIssuingJobs.SingleAsync(ct)).ReceptionStartedAt);
            entered.TrySetResult();
            await resume.Task.WaitAsync(ct);
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var recovery = scope.Submission.RecoverReceptionAsync(original.Id, Grounds, timeout.Token);
        Task<SriReceptionRecoveryStatus>? competing = null;
        try
        {
            await fence.Verified.Task.WaitAsync(timeout.Token);
            // Race after a successful ownership renewal, before invoking HTTP. Even a worker
            // that ignores the future lease cannot claim the affected row while dispatch holds it.
            var available = await duplicate.Db.Database.SqlQuery<int>($"""
                SELECT 1 AS "Value" FROM "ElectronicIssuingJobs"
                WHERE "SaleId" = {original.Id} FOR UPDATE SKIP LOCKED
                """).ToListAsync(timeout.Token);
            Assert.Empty(available);
            Assert.DoesNotContain("reception", soap.Calls);
            fence.Continue.TrySetResult();
            await entered.Task.WaitAsync(timeout.Token);
            Assert.Empty(await duplicate.Coordinator.ClaimBatchAsync(1, timeout.Token));
            competing = duplicate.Submission.RecoverReceptionAsync(original.Id, Grounds, timeout.Token);
        }
        finally { fence.Continue.TrySetResult(); resume.TrySetResult(); }
        Assert.Equal(FiscalRecoveryEvidence.Received, (await recovery).Code);
        var competingError = await Assert.ThrowsAsync<InvalidOperationException>(async () => await competing!);
        Assert.Contains(competingError.Message, new[] { "FISCAL_DOCUMENT_BUSY", FiscalRecoveryEvidence.Received });
        Assert.Equal(new[] { "authorization", "validity", "reception" }, soap.Calls);
        Assert.Equal(original.SriSignedXml, soap.Transmitted);

        await using var verify = db.CreateDbContext();
        var updated = await verify.Sales.SingleAsync();
        Assert.Equal(original.AccessKey, updated.AccessKey);
        Assert.Equal(original.Number, updated.Number);
        Assert.Equal(original.Total, updated.Total);
        Assert.Equal(original.SriSignedXml, updated.SriSignedXml);
        Assert.Equal(original.SriXmlDraft, updated.SriXmlDraft);
        Assert.Single(await verify.InventoryMovements.ToListAsync());
        Assert.Single(await verify.DocumentSequences.ToListAsync());
        var extra = await verify.SriSubmissionAttempts.SingleAsync(a => a.AttemptType == SriSubmissionAttemptType.ExtraordinaryReception);
        Assert.Equal(Grounds.Reference, extra.RecoveryReference);
        Assert.Equal(Grounds.Reason, extra.RecoveryReason);
        Assert.Equal(ElectronicIssuingCoordinator.Hash(original.SriSignedXml), extra.RecoverySignedXmlHash);
        Assert.Equal(fixture.Admin.UserId, extra.CreatedByUserId);
        Assert.NotNull(extra.RecoveryCompletedAt);
        Assert.Equal("RECIBIDA", extra.ReceptionStatus);

        var duplicateRow = new SriSubmissionAttempt
        {
            SaleId = original.Id, CompanyId = original.CompanyId, EstablishmentId = original.EstablishmentId,
            EmissionPointId = original.EmissionPointId, AccessKey = original.AccessKey!, Environment = 1,
            AttemptType = SriSubmissionAttemptType.ExtraordinaryReception, CreatedByUserId = fixture.Admin.UserId,
            CreatedAt = DateTime.UtcNow, RecoveryReference = Grounds.Reference, RecoveryReason = Grounds.Reason,
            RecoverySignedXmlHash = extra.RecoverySignedXmlHash, RecoveryEvidenceId = extra.RecoveryEvidenceId
        };
        verify.SriSubmissionAttempts.Add(duplicateRow);
        var unique = await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(unique.InnerException).SqlState);
        await using var migrate = db.CreateDbContext();
        var down = await Assert.ThrowsAsync<PostgresException>(() => migrate.GetService<IMigrator>()
            .MigrateAsync("20261119120000_ReconcileTenantAdminBatchPricePermission"));
        Assert.Contains("history prevents migration rollback", down.MessageText);
    }

    [Theory]
    [InlineData("generic")]
    [InlineData("wrong-key")]
    [InlineData("missing-count")]
    [InlineData("invalid-count")]
    [InlineData("explicit-pending")]
    [InlineData("date-window")]
    [InlineData("wrong-validity-key")]
    [InlineData("validity-timeout")]
    [InlineData("validity-record")]
    [InlineData("validity-record-with-message")]
    public async Task Negative_admission_requires_two_strict_distinct_affirmative_empty_results(string scenario)
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original) { Scenario = scenario };
        await using var scope = Scope(fixture, soap);
        var result = await scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default);
        Assert.DoesNotContain("reception", soap.Calls);
        Assert.Equal(SaleDocumentStatus.Draft, (await scope.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus);
        Assert.False(await scope.Db.SriSubmissionAttempts.AnyAsync(a => a.AttemptType == SriSubmissionAttemptType.ExtraordinaryReception));
        if (scenario is "validity-record" or "validity-record-with-message")
        {
            Assert.Equal(FiscalRecoveryEvidence.ValidityRecord, result.Code);
            Assert.Equal(FiscalRecoveryEvidence.ValidityRecord, (await scope.Submission.GetReceptionRecoveryStatusAsync(original.Id, default)).Code);
            using var later = new Soap(original);
            await using var retry = Scope(fixture, later);
            await Conflict(FiscalRecoveryEvidence.ValidityRecord, () => retry.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
            Assert.Empty(later.Calls);
        }
        else if (scenario is "date-window" or "wrong-validity-key" or "validity-timeout")
        {
            Assert.Equal(FiscalRecoveryEvidence.ValidityInconclusive, result.Code);
            Assert.True(await scope.Db.SriSubmissionAttempts.AnyAsync(a => a.AttemptType == SriSubmissionAttemptType.Validity
                && a.Status == SriSubmissionAttemptStatus.Failed));
        }
        else Assert.DoesNotContain("validity", soap.Calls);
    }

    [Theory]
    [InlineData("same", "FISCAL_RECOVERY_AUTHORIZED")]
    [InlineData("different", FiscalRecoveryEvidence.AuthorizationUnverified)]
    [InlineData("missing", FiscalRecoveryEvidence.AuthorizationUnverified)]
    [InlineData("bad-environment", FiscalRecoveryEvidence.AuthorizationUnverified)]
    [InlineData("same-43", "FISCAL_RECOVERY_AUTHORIZED")]
    [InlineData("different-70", FiscalRecoveryEvidence.Processing)]
    [InlineData("same-45", FiscalRecoveryEvidence.SequenceConflict)]
    public async Task Positive_authorization_requires_the_original_signed_payload_and_never_reconciles_45(string scenario, string expected)
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original) { Scenario = scenario };
        await using var scope = Scope(fixture, soap);
        Assert.Equal(expected, (await scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default)).Code);
        Assert.Equal(new[] { "authorization" }, soap.Calls);
        Assert.Equal(expected == "FISCAL_RECOVERY_AUTHORIZED",
            (await scope.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus == SaleDocumentStatus.Authorized);
    }

    [Theory]
    [InlineData("rejected-same", "SRI_AUTHORIZATION_REJECTED")]
    [InlineData("rejected-wrong-xml", FiscalRecoveryEvidence.AuthorizationUnverified)]
    [InlineData("rejected-wrong-environment", FiscalRecoveryEvidence.AuthorizationUnverified)]
    [InlineData("rejected-missing-xml", FiscalRecoveryEvidence.AuthorizationUnverified)]
    public async Task Rejection_is_terminal_only_for_the_original_signed_invoice(string scenario, string expected)
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original) { Scenario = scenario };
        await using var scope = Scope(fixture, soap);
        Assert.Equal(expected, (await scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default)).Code);
        Assert.Equal(new[] { "authorization" }, soap.Calls);
        Assert.Equal(expected == "SRI_AUTHORIZATION_REJECTED",
            (await scope.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus == SaleDocumentStatus.Rejected);
    }

    [Theory]
    [InlineData("43", FiscalRecoveryEvidence.Reconcile)]
    [InlineData("70", FiscalRecoveryEvidence.Processing)]
    [InlineData("45", FiscalRecoveryEvidence.SequenceConflict)]
    [InlineData("RECIBIDA", FiscalRecoveryEvidence.Received)]
    public async Task Historical_reception_evidence_survives_newer_empty_queries(string signal, string expected)
    {
        var (fixture, original) = await SeedAsync();
        await using (var write = db.CreateDbContext())
        {
            var audit = await write.SriSubmissionAttempts.SingleAsync();
            if (signal == "RECIBIDA") audit.ReceptionStatus = signal;
            else audit.ResponseXml = $"<return><mensaje><identificador>12</identificador></mensaje><mensaje><identificador>{signal}</identificador></mensaje></return>";
            var sale = await write.Sales.SingleAsync();
            sale.SriAuthorizationStatus = "PENDIENTE";
            await write.SaveChangesAsync();
        }
        using var soap = new Soap(original);
        await using var scope = Scope(fixture, soap);
        Assert.Equal(expected, (await scope.Submission.GetReceptionRecoveryStatusAsync(original.Id, default)).Code);
        await Conflict(expected, () => scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        Assert.Empty(soap.Calls);
    }

    [Theory]
    [InlineData("43", FiscalRecoveryEvidence.Reconcile)]
    [InlineData("70", FiscalRecoveryEvidence.Processing)]
    [InlineData("45", FiscalRecoveryEvidence.SequenceConflict)]
    public async Task Duplicate_and_processing_reception_results_only_allow_verified_reconciliation(string code, string status)
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original) { Scenario = "return-" + code };
        await using var manual = Scope(fixture, soap);
        Assert.Equal(status, (await manual.Submission.RecoverReceptionAsync(original.Id, Grounds, default)).Code);
        Assert.Equal(SaleDocumentStatus.PendingAuthorization, (await manual.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus);
        Assert.Equal(status, (await manual.Submission.GetReceptionRecoveryStatusAsync(original.Id, default)).Code);
        await Conflict(status, () => manual.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        if (code == "45")
        {
            await Conflict(status, () => manual.Submission.CheckAuthorizationAsync(original.Id));
            return;
        }

        await fixture.DueAsync(original.Id);
        using var mismatch = new Soap(original) { Scenario = "different" };
        await using (var worker = Scope(fixture, mismatch))
            await worker.Processor.ProcessAsync(await fixture.ClaimAsync(), default);
        Assert.Equal(new[] { "authorization" }, mismatch.Calls);
        Assert.NotEqual(SaleDocumentStatus.Authorized, (await manual.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus);

        await fixture.DueAsync(original.Id);
        using var matching = new Soap(original) { Scenario = "same" };
        await using (var worker = Scope(fixture, matching))
            await worker.Processor.ProcessAsync(await fixture.ClaimAsync(), default);
        Assert.Equal(new[] { "authorization" }, matching.Calls);
        Assert.Equal(SaleDocumentStatus.Authorized, (await manual.Db.Sales.AsNoTracking().SingleAsync()).DocumentStatus);
    }

    [Theory]
    [InlineData("fenced-before", 0)]
    [InlineData("cancel-after", 1)]
    [InlineData("timeout-after", 1)]
    public async Task Consumed_admission_survives_interruption_and_worker_restart(string scenario, int posts)
    {
        var (fixture, original) = await SeedAsync();
        using var soap = new Soap(original);
        using var cancellation = new CancellationTokenSource();
        var fence = new FenceBeforePost(fixture, original.Id);
        await using var scope = Scope(fixture, soap, interceptors: scenario == "fenced-before" ? [fence] : []);
        soap.BeforeReply = (operation, ct) =>
        {
            if (operation == "reception")
            {
                if (scenario == "timeout-after") throw new HttpRequestException();
                if (scenario == "cancel-after") { cancellation.Cancel(); ct.ThrowIfCancellationRequested(); }
            }
            return Task.CompletedTask;
        };
        if (scenario == "fenced-before")
            await Conflict("FISCAL_LEASE_LOST", () => scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        else if (scenario == "cancel-after")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Submission.RecoverReceptionAsync(original.Id, Grounds, cancellation.Token));
        else Assert.Equal(FiscalRecoveryEvidence.OutcomeUnknown, (await scope.Submission.RecoverReceptionAsync(original.Id, Grounds, default)).Code);

        Assert.Equal(posts, soap.Calls.Count(c => c == "reception"));
        await fixture.DueAsync(original.Id, expire: true);
        fixture.Transport.Authorization = _ => Task.FromResult(new SriAuthorizationResponse { Estado = "PENDIENTE" });
        await fixture.ProcessNextAsync();
        Assert.Equal(1, fixture.Transport.Submissions); // Only the original; the worker did not resend on recovery.
        await using var restarted = Scope(fixture, soap);
        Assert.Equal(FiscalRecoveryEvidence.OutcomeUnknown, (await restarted.Submission.GetReceptionRecoveryStatusAsync(original.Id, default)).Code);
        await Conflict(FiscalRecoveryEvidence.OutcomeUnknown, () => restarted.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
    }

    [Fact]
    public async Task Permissions_context_and_revocation_after_fresh_query_are_enforced()
    {
        var (fixture, original) = await SeedAsync();
        var other = new AutonomousIssuingFixture(db);
        await other.InitializeAsync("other-183", 984);
        using var soap = new Soap(original);
        await using var forbidden = Scope(fixture, soap, fixture.Tenant.OperationalContext);
        await Conflict("FISCAL_PERMISSION_REQUIRED", () => forbidden.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        await using var foreign = Scope(fixture, soap, other.Admin);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => foreign.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        await using var authorized = Scope(fixture, soap);
        await Conflict("FISCAL_RECOVERY_CONFIRMATION_AND_REASON_REQUIRED",
            () => authorized.Submission.RecoverReceptionAsync(original.Id,
                Grounds with { ConfirmsResendDespiteUncertainReception = false }, default));
        Assert.Empty(soap.Calls);
        soap.BeforeReply = async (operation, ct) =>
        {
            if (operation != "validity") return;
            await using var write = db.CreateDbContext();
            var roleId = await write.Users.Where(u => u.Id == fixture.Admin.UserId).Select(u => u.RoleId).SingleAsync(ct);
            await write.RolePermissions.Where(r => r.RoleId == roleId && r.Permission.Code == AppPermissions.SriDocumentsSubmit).ExecuteDeleteAsync(ct);
        };
        await Conflict("FISCAL_PERMISSION_REQUIRED", () => authorized.Submission.RecoverReceptionAsync(original.Id, Grounds, default));
        Assert.Equal(new[] { "authorization", "validity" }, soap.Calls);
    }

    private FiscalWorkerTestScope Scope(AutonomousIssuingFixture fixture, Soap handler, OperationalContext? actor = null,
        IInterceptor[]? interceptors = null)
    {
        var options = Options.Create(new SriOptions { AuthorizationTestUrl = "https://synthetic.invalid/authorization",
            ReceptionTestUrl = "https://synthetic.invalid/reception", ValidityTestUrl = "https://synthetic.invalid/validity" });
        var client = new SriWebServiceClient(new HttpClient(handler, disposeHandler: false), options, NullLogger<SriWebServiceClient>.Instance);
        return new(db.CreateDbContext(interceptors ?? []), fixture.Protection, client, options, new StaticOperationalContextAccessor(actor ?? fixture.Admin));
    }

    private static async Task Conflict(string code, Func<Task> action)
        => Assert.Equal(code, (await Assert.ThrowsAsync<InvalidOperationException>(action)).Message);

    private sealed class FenceBeforePost(AutonomousIssuingFixture fixture, int saleId) : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (IsDispatchVerification(command))
                await fixture.DueAsync(saleId, expire: true);
            return result;
        }
    }

    private static bool IsDispatchVerification(DbCommand command)
        => command.CommandText.Contains("SET \"LeaseExpiresAt\" = clock_timestamp() + interval", StringComparison.Ordinal)
            && command.CommandText.Contains("> clock_timestamp() - interval", StringComparison.Ordinal);

    private sealed class HoldAfterDispatchVerification : DbCommandInterceptor
    {
        public TaskCompletionSource Verified { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken ct = default)
        {
            if (IsDispatchVerification(command))
            {
                Assert.Equal(1, result);
                Assert.NotNull(command.Transaction);
                Verified.TrySetResult();
                await Continue.Task.WaitAsync(ct);
            }
            return result;
        }
    }

    private sealed class Soap(Sale sale) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        public string Scenario { get; init; } = "both-empty";
        public string? Transmitted { get; private set; }
        public Func<string, CancellationToken, Task>? BeforeReply { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("synthetic.invalid", request.RequestUri!.Host);
            var operation = request.RequestUri.AbsolutePath.Trim('/');
            var xml = XDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Calls.Add(operation);
            if (operation == "reception")
                Transmitted = Encoding.UTF8.GetString(Convert.FromBase64String(xml.Descendants("xml").Single().Value));
            else
                Assert.Equal(sale.AccessKey, xml.Descendants(operation == "validity" ? "claveAcceso" : "claveAccesoComprobante").Single().Value);
            if (BeforeReply is not null) await BeforeReply(operation, ct);
            if (Scenario == "validity-timeout" && operation == "validity") throw new HttpRequestException();
            XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
            var body = operation switch
            {
                "authorization" => Authorization(),
                "validity" => Validity(),
                _ => Scenario.StartsWith("return-", StringComparison.Ordinal)
                    ? new XElement("respuesta", new XElement("estado", "DEVUELTA"), new XElement("mensajes",
                        new XElement("mensaje", new XElement("identificador", "12")),
                        new XElement("mensaje", new XElement("identificador", Scenario[7..]))))
                    : new XElement("respuesta", new XElement("estado", "RECIBIDA"))
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(new XElement(soap + "Envelope",
                new XElement(soap + "Body", body)).ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml") };
        }

        private XElement Authorization()
        {
            XNamespace ns = "http://ec.gob.sri.ws.autorizacion";
            var key = Scenario == "wrong-key" ? new string('2', 49) : sale.AccessKey;
            if (Scenario == "generic") return new XElement("generic");
            var rejected = Scenario.StartsWith("rejected-", StringComparison.Ordinal);
            var positive = Scenario.StartsWith("same", StringComparison.Ordinal) || Scenario.StartsWith("different", StringComparison.Ordinal)
                || Scenario is "missing" or "bad-environment" || rejected;
            var code = Scenario.Contains('-') ? Scenario.Split('-')[^1] : null;
            return new(ns + "autorizacionComprobanteResponse", new XElement("RespuestaAutorizacionComprobante",
                new XElement("claveAccesoConsultada", key),
                Scenario == "missing-count" ? null : new XElement("numeroComprobantes", positive ? "1" : Scenario == "invalid-count" ? "0.0" : "0"),
                Scenario == "explicit-pending" ? new XElement("estado", "PENDIENTE") : null,
                !positive ? null : new XElement("autorizaciones", new XElement("autorizacion",
                    new XElement("estado", rejected ? "NO AUTORIZADO" : "AUTORIZADO"), rejected ? null : new XElement("numeroAutorizacion", key),
                    new XElement("ambiente", Scenario is "bad-environment" or "rejected-wrong-environment" ? "PRODUCCION" : "PRUEBAS"),
                    Scenario is "missing" or "rejected-missing-xml" ? null : new XElement("comprobante", new XCData(Scenario.StartsWith("different", StringComparison.Ordinal) || Scenario == "rejected-wrong-xml"
                        ? $"<factura><claveAcceso>{key}</claveAcceso></factura>" : sale.SriSignedXml!)),
                    code is "43" or "45" or "70" ? new XElement("mensajes", new XElement("mensaje",
                        new XElement("identificador", "12")), new XElement("mensaje", new XElement("identificador", code))) : null))));
        }

        private XElement Validity()
        {
            XNamespace ns = "http://ec.gob.sri.ws.consultas";
            return new(ns + "consultarEstadoAutorizacionComprobanteResponse", new XElement("EstadoAutorizacionComprobante",
                new XElement("claveAcceso", Scenario == "wrong-validity-key" ? new string('2', 49) : sale.AccessKey),
                Scenario == "validity-record" ? new XElement("estadoAutorizacion", "ANULADO") :
                Scenario == "validity-record-with-message" ? new object[] { new XElement("estadoConsulta", "ACEPTADA"),
                    new XElement("estadoAutorizacion", "AUTORIZADO"),
                    new XElement("mensajes", new XElement("mensaje", new XElement("identificador", "12"))) } :
                    new object[] { new XElement("estadoConsulta", "RECHAZADA"),
                        new XElement("mensajes", new XElement("mensaje", new XElement("identificador", "99"),
                            new XElement("informacionAdicional", Scenario == "date-window"
                                ? "No es posible validar la clave de acceso ya que la fecha de emision esta fuera del rango permitido." : NoData))) }));
        }
    }
}
