using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ProductPriceBatchService(PosDbContext db, IOperationalContextAccessor accessor,
    TenantAdministrationGuard guard, IProductCostService costs,
    IBusinessClockService clock, IDataProtectionProvider dataProtection)
{
    // Bound price locks, proof size and the complete per-product audit to one reviewed batch.
    public const int MaxProducts = ProductBatchSelection.MaxProducts;
    private const int RecentReceiptLimit = 20;
    private const string Kind = "product-price-batch";
    private static readonly string[] Permissions =
        [AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductPricesBatch];
    private readonly IDataProtector protector = dataProtection.CreateProtector("ProductPriceBatch.Preview.v1");

    public async Task<ProductFiscalBatchCandidates> CandidatesAsync(string? search, bool? active,
        int? categoryId, string? fiscalStatus, ProductVatCategory? vatCategory, CancellationToken cancellationToken = default)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(actor);
        return await ProductBatchSelection.CandidatesAsync(db, actor, search, active, categoryId, fiscalStatus, vatCategory, cancellationToken);
    }

    public async Task<ProductPriceBatchPreview> PreviewAsync(ProductPriceBatchRequest request)
    {
        var actor = await accessor.GetRequiredContextAsync();
        request = Normalize(request);
        await using var tx = await db.Database.BeginTransactionAsync();
        await guard.LockOperationalWriteAsync(actor);
        await RequirePermissionsAsync(actor);
        var products = await db.Products.AsNoTracking().Where(p => p.CompanyId == actor.CompanyId
            && request.ProductIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var now = clock.UtcNow;
        var operationId = Guid.NewGuid();
        var snapshot = await EvaluateAsync(actor, request, products);
        var expiresAt = now.AddMinutes(15);
        var token = snapshot.Rejected == 0 ? protector.Protect(JsonSerializer.Serialize(new PreviewProof(
            operationId, actor.CompanyId, actor.UserId, actor.EstablishmentId, actor.EmissionPointId, expiresAt,
            request, snapshot.Fingerprint))) : null;
        await tx.CommitAsync();
        return new(operationId, actor.CompanyId, snapshot.CompanyName, request.Reason,
            request.Action, request.Value, snapshot.Rows, snapshot.Changed, snapshot.Unchanged, snapshot.Rejected,
            token, token is null ? null : expiresAt);
    }

    public async Task<ProductPriceBatchResult> ConfirmAsync(ProductPriceBatchConfirmation confirmation)
    {
        if (!confirmation.Confirmed || confirmation.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(confirmation.PreviewToken) || confirmation.PreviewToken.Length > 24576)
            throw Error("PRICE_BATCH_CONFIRMATION_REQUIRED", 400);

        var actor = await accessor.GetRequiredContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        // Same order as sales, purchases and catalog writers: company, then ascending product IDs.
        await guard.LockExclusiveOperationalWriteAsync(actor);
        await RequirePermissionsAsync(actor);
        var tokenHash = CriticalOperationRequest.Hash(confirmation.PreviewToken);
        var previous = await FindReceiptAsync(actor.CompanyId, confirmation.OperationId);
        if (previous is not null)
        {
            if (!OwnReceipt(previous, actor) || previous.PayloadHash != tokenHash)
                throw Error("PRICE_BATCH_REQUEST_CONFLICT", 409);
            return ReadReceipt(previous);
        }

        PreviewProof? proof;
        try { proof = JsonSerializer.Deserialize<PreviewProof>(protector.Unprotect(confirmation.PreviewToken)); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException)
        { throw Error("PRICE_BATCH_PREVIEW_INVALID", 409); }
        if (proof is null || proof.OperationId != confirmation.OperationId || proof.CompanyId != actor.CompanyId
            || proof.UserId != actor.UserId || proof.EstablishmentId != actor.EstablishmentId
            || proof.EmissionPointId != actor.EmissionPointId)
            throw Error("PRICE_BATCH_PREVIEW_INVALID", 409);

        var products = await costs.LockProductsAsync(actor.CompanyId, proof.Request.ProductIds);
        var now = clock.UtcNow;
        if (now >= proof.ExpiresAt) throw Error("PRICE_BATCH_PREVIEW_EXPIRED", 409);
        var snapshot = await EvaluateAsync(actor, proof.Request, products);
        if (snapshot.Rejected != 0 || snapshot.Fingerprint != proof.Fingerprint)
            throw Error("PRICE_BATCH_PREVIEW_CHANGED", 409);
        var result = new ProductPriceBatchResult(proof.OperationId, actor.CompanyId, snapshot.CompanyName,
            actor.UserId, now, proof.Request.Reason, proof.Request.Action, proof.Request.Value,
            snapshot.Rows, snapshot.Changed, snapshot.Unchanged, snapshot.Rejected);

        try
        {
            foreach (var row in snapshot.Rows.Where(r => r.Outcome == "changed"))
                products[row.ProductId].Price = row.ProposedPrice!.Value;
            db.InitialDataBatches.Add(new InitialDataBatch
            {
                CompanyId = actor.CompanyId, UserId = actor.UserId, EstablishmentId = actor.EstablishmentId,
                EmissionPointId = actor.EmissionPointId, Kind = Kind, RequestId = result.OperationId,
                PayloadHash = tokenHash, RowCount = result.Rows.Length, CreatedAt = now,
                ResultJson = JsonSerializer.Serialize(result)
            });
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return result;
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ProductPriceBatchResult> ReceiptAsync(Guid operationId)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(actor);
        var batch = await FindReceiptAsync(actor.CompanyId, operationId);
        return batch is not null && OwnReceipt(batch, actor) ? ReadReceipt(batch)
            : throw Error("PRICE_BATCH_RESULT_NOT_FOUND", 404);
    }

    public async Task<ProductPriceBatchReceiptSummary[]> RecentReceiptsAsync(CancellationToken cancellationToken = default)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(actor);
        return await db.InitialDataBatches.AsNoTracking()
            .Where(b => b.CompanyId == actor.CompanyId && b.Kind == Kind && b.UserId == actor.UserId
                && b.EstablishmentId == actor.EstablishmentId && b.EmissionPointId == actor.EmissionPointId)
            .OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id).Take(RecentReceiptLimit)
            .Select(b => new ProductPriceBatchReceiptSummary(b.RequestId, b.CreatedAt, b.RowCount))
            .ToArrayAsync(cancellationToken);
    }

    private async Task<Evaluation> EvaluateAsync(OperationalContext actor, ProductPriceBatchRequest request,
        IReadOnlyDictionary<int, Product> products)
    {
        var companyName = await db.Companies.Where(c => c.Id == actor.CompanyId).Select(c => c.Name).SingleAsync();
        var classifications = await db.ProductFiscalClassifications.AsNoTracking()
            .Where(c => c.CompanyId == actor.CompanyId && request.ProductIds.Contains(c.ProductId))
            .GroupBy(c => c.ProductId).Select(g => g.OrderByDescending(c => c.Revision).First()).ToDictionaryAsync(c => c.ProductId);
        var rows = new List<ProductPriceBatchRow>(request.ProductIds.Length);
        var states = new List<string>(products.Count);
        foreach (var id in request.ProductIds)
        {
            if (!products.TryGetValue(id, out var p))
            {
                rows.Add(new(id, null, null, null, null, null, null, null, "rejected", "PRODUCT_NOT_FOUND", []));
                continue;
            }
            classifications.TryGetValue(id, out var classification);
            states.Add(ProductEditorService.Token(p, classification));
            var calculation = ProductPriceCalculation.Evaluate(p.Price, p.Cost, request.Action, request.Value);
            var transportSafe = ProductPriceCalculation.TransportSafe(p.Price, p.Cost);
            var warnings = calculation.Warnings.ToList();
            if (!p.IsActive) warnings.Add("PRODUCT_INACTIVE");
            rows.Add(new(id, p.Name, p.InternalCode, transportSafe ? p.Price : null, transportSafe ? p.Cost : null, calculation.Price,
                transportSafe ? DecimalToken(ProductPriceCalculation.Margin(p.Price, p.Cost)) : null,
                calculation.Price is { } price ? DecimalToken(ProductPriceCalculation.Margin(price, p.Cost)) : null,
                calculation.Error is not null ? "rejected" : calculation.Price == p.Price ? "unchanged" : "changed",
                calculation.Error, warnings.ToArray()));
        }
        return new(companyName, rows.ToArray(), rows.Count(r => r.Outcome == "changed"),
            rows.Count(r => r.Outcome == "unchanged"), rows.Count(r => r.Outcome == "rejected"),
            CriticalOperationRequest.Hash(new { companyName, states, Rows = rows.Select(r => new {
                r.ProductId, r.Name, r.InternalCode, CurrentPrice = DecimalToken(r.CurrentPrice),
                Cost = DecimalToken(r.Cost), ProposedPrice = DecimalToken(r.ProposedPrice),
                r.CurrentMargin, r.ProposedMargin,
                r.Outcome, r.Rejection, r.Warnings }) }));
    }

    private async Task RequirePermissionsAsync(OperationalContext actor)
    {
        try { await ElectronicIssuingCoordinator.RequirePermissionsAsync(db, actor.UserId, actor.CompanyId, Permissions); }
        catch (InvalidOperationException e) when (e.Message == "FISCAL_PERMISSION_REQUIRED")
        { throw Error("PRICE_BATCH_PERMISSION_REQUIRED", 403); }
    }

    private Task<InitialDataBatch?> FindReceiptAsync(int companyId, Guid id) =>
        db.InitialDataBatches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.RequestId == id);

    private static bool OwnReceipt(InitialDataBatch b, OperationalContext a) => b.Kind == Kind
        && b.UserId == a.UserId && b.EstablishmentId == a.EstablishmentId && b.EmissionPointId == a.EmissionPointId;

    private static ProductPriceBatchResult ReadReceipt(InitialDataBatch b) =>
        JsonSerializer.Deserialize<ProductPriceBatchResult>(b.ResultJson)!;

    private static ProductPriceBatchRequest Normalize(ProductPriceBatchRequest request)
    {
        if (request.ProductIds is not { Length: > 0 and <= MaxProducts } ids
            || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw Error("PRICE_BATCH_SELECTION_INVALID", 400);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            throw Error("PRICE_BATCH_REASON_INVALID", 400);
        ProductPriceCalculation.Validate(request.Action, request.Value);
        return request with { ProductIds = ids.Order().ToArray(), Reason = request.Reason.Trim() };
    }

    private static OperationalContextException Error(string code, int status) => new(code, status);

    private static string? DecimalToken(decimal? value) => value?.ToString("G29", CultureInfo.InvariantCulture);

    private sealed record PreviewProof(Guid OperationId, int CompanyId, int UserId, int EstablishmentId,
        int EmissionPointId, DateTime ExpiresAt, ProductPriceBatchRequest Request, string Fingerprint);

    private sealed record Evaluation(string CompanyName, ProductPriceBatchRow[] Rows,
        int Changed, int Unchanged, int Rejected, string Fingerprint);
}
