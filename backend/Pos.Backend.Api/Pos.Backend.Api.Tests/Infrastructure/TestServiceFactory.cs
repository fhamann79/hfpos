using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Tests.Infrastructure;

internal sealed class TestServiceScope : IAsyncDisposable
{
    public TestServiceScope(
        PostgresDatabaseFixture database,
        OperationalContext operationalContext,
        params IInterceptor[] interceptors)
    {
        DbContext = database.CreateDbContext(interceptors);

        var contextAccessor = new StaticOperationalContextAccessor(operationalContext);
        var businessClock = new FixedBusinessClock();
        var fiscalClock = new FixedSriFiscalClock(businessClock.UtcNow);
        var administrationGuard = new TenantAdministrationGuard(DbContext);

        Inventory = new InventoryService(
            DbContext,
            NullLogger<InventoryService>.Instance,
            contextAccessor,
            businessClock,
            administrationGuard);

        CashSessions = new CashSessionService(
            DbContext,
            NullLogger<CashSessionService>.Instance,
            contextAccessor,
            businessClock,
            administrationGuard);

        PurchaseReceipts = new PurchaseReceiptQueryService(DbContext, contextAccessor);

        var documentNumbers = new FiscalDocumentNumberService(
            DbContext,
            NullLogger<FiscalDocumentNumberService>.Instance,
            fiscalClock);

        Sales = new SalesService(
            DbContext,
            NullLogger<SalesService>.Instance,
            contextAccessor,
            Inventory,
            CashSessions,
            documentNumbers,
            UnexpectedSriDependency.Instance,
            UnexpectedSriDependency.Instance,
            fiscalClock,
            businessClock,
            UnexpectedSriDependency.Instance,
            Options.Create(new SriOptions()),
            administrationGuard);
    }

    public PosDbContext DbContext { get; }

    public InventoryService Inventory { get; }

    public CashSessionService CashSessions { get; }

    public PurchaseReceiptQueryService PurchaseReceipts { get; }

    public SalesService Sales { get; }

    public ValueTask DisposeAsync() => DbContext.DisposeAsync();
}

internal sealed class StaticOperationalContextAccessor(OperationalContext operationalContext)
    : IOperationalContextAccessor
{
    public Task<OperationalContext> GetRequiredContextAsync()
        => Task.FromResult(operationalContext);
}

internal sealed class FixedBusinessClock : IBusinessClockService
{
    private readonly BusinessClockService _inner = new();

    public DateTime UtcNow { get; } = new(2026, 9, 18, 20, 0, 0, DateTimeKind.Utc);

    public TimeZoneInfo ResolveTimeZone(string timeZoneId)
        => _inner.ResolveTimeZone(timeZoneId);

    public DateOnly GetBusinessDate(DateTime utcInstant, string timeZoneId)
        => _inner.GetBusinessDate(utcInstant, timeZoneId);

    public DateTime GetBusinessDateStartUtc(DateOnly businessDate, string timeZoneId)
        => _inner.GetBusinessDateStartUtc(businessDate, timeZoneId);

    public DateTime GetBusinessDateEndExclusiveUtc(DateOnly businessDate, string timeZoneId)
        => _inner.GetBusinessDateEndExclusiveUtc(businessDate, timeZoneId);

    public BusinessDateRange GetBusinessDateRangeUtc(
        DateOnly from,
        DateOnly toInclusive,
        string timeZoneId)
        => _inner.GetBusinessDateRangeUtc(from, toInclusive, timeZoneId);
}

internal sealed class FixedSriFiscalClock(DateTime utcNow) : ISriFiscalClock
{
    public DateTime UtcNow { get; } = utcNow;

    public DateOnly GetEcuadorFiscalDate(DateTime utcInstant)
        => DateOnly.FromDateTime(utcInstant.AddHours(-5));
}

internal sealed class UnexpectedSriDependency :
    ISriAccessKeyService,
    ISriXmlDraftService,
    ISriInvoiceXmlValidator
{
    public static UnexpectedSriDependency Instance { get; } = new();

    private UnexpectedSriDependency()
    {
    }

    public SriAccessKeyResult GenerateInvoiceAccessKey(SriAccessKeyRequest request)
        => throw UnexpectedCall();

    public SriAccessKeyResult GenerateCreditNoteAccessKey(SriAccessKeyRequest request)
        => throw UnexpectedCall();

    public int CalculateModulo11CheckDigit(string accessKeyBase48)
        => throw UnexpectedCall();

    public string GenerateInvoiceXmlDraft(SriXmlDraftRequest request)
        => throw UnexpectedCall();

    public void ValidateUnsignedInvoiceXml(string xml)
        => throw UnexpectedCall();

    private static InvalidOperationException UnexpectedCall()
        => new("SRI dependencies must not be invoked by ticket-sale integration tests.");
}
