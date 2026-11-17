using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class ProductFiscalBatchService(PosDbContext db, IOperationalContextAccessor accessor,
    TenantAdministrationGuard guard, IProductCostService costs, IFiscalProfileService profiles,
    IBusinessClockService clock, ISriFiscalClock fiscalClock, IDataProtectionProvider dataProtection)
{
    // Covers the ~228 legacy items in #155 with bounded locks, request and audit payloads.
    public const int MaxProducts = 250;
    private const string Kind = "product-fiscal-batch";
    private static readonly string[] Permissions =
        [AppPermissions.CatalogProductsRead, AppPermissions.CatalogProductsWrite,
         AppPermissions.FiscalSettingsRead, AppPermissions.FiscalSettingsWrite];
    private readonly IDataProtector protector = dataProtection.CreateProtector("ProductFiscalBatch.Preview.v1");

    public async Task<PagedResultDto<ProductFiscalBatchCandidate>> PageAsync(string? search, bool? active,
        int? categoryId, string? fiscalStatus, int page, int pageSize)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(actor);
        if (search?.Length > 150 || fiscalStatus is not (null or "pending" or "stale" or "current"))
            throw Error("FISCAL_BATCH_FILTER_INVALID", 400);
        var products = db.Products.AsNoTracking().Where(p => p.CompanyId == actor.CompanyId);
        if (active.HasValue) products = products.Where(p => p.IsActive == active.Value);
        if (categoryId.HasValue) products = products.Where(p => p.CategoryId == categoryId.Value);
        if (search?.Trim() is { Length: > 0 } text)
        {
            var term = text.ToLowerInvariant();
            products = products.Where(p => p.Name.ToLower().Contains(term)
                || (p.Barcode != null && p.Barcode.ToLower().Contains(term))
                || (p.InternalCode != null && p.InternalCode.ToLower().Contains(term)));
        }

        // "Current" describes the declaration, not the company's profile or ability to sell.
        var candidates = from p in products
            from c in db.ProductFiscalClassifications.AsNoTracking().Where(c => c.CompanyId == p.CompanyId && c.ProductId == p.Id)
                .OrderByDescending(c => c.Revision).Take(1).DefaultIfEmpty()
            select new { p.Id, p.Name, p.InternalCode, p.Barcode, p.CategoryId,
                p.IsActive, p.VatCategory, FiscalStatus = c == null ? "pending" :
                    c.Confirmed && c.ProductFiscalRevision == p.FiscalRevision && c.DeclaredVatCategory == p.VatCategory
                    ? "current" : "stale" };
        if (fiscalStatus is not null) candidates = candidates.Where(p => p.FiscalStatus == fiscalStatus);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var total = await candidates.CountAsync();
        var pages = (int)Math.Ceiling((double)total / pageSize);
        var items = page > pages ? [] : await candidates.OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new ProductFiscalBatchCandidate(p.Id, p.Name, p.InternalCode, p.Barcode,
                p.CategoryId, p.IsActive, p.VatCategory, p.FiscalStatus)).ToArrayAsync();
        return new() { Items = items, TotalItems = total, TotalPages = pages, Page = page, PageSize = pageSize };
    }

    public async Task<ProductFiscalBatchPreview> PreviewAsync(ProductFiscalBatchRequest request)
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
        var snapshot = await EvaluateAsync(actor, request, products, now);
        var expiresAt = now.AddMinutes(15);
        var token = snapshot.Rejected == 0 ? protector.Protect(JsonSerializer.Serialize(new PreviewProof(
            operationId, actor.CompanyId, actor.UserId, actor.EstablishmentId, actor.EmissionPointId, expiresAt,
            request, snapshot.Fingerprint))) : null;
        await tx.CommitAsync();
        return new(operationId, actor.CompanyId, snapshot.CompanyName, snapshot.Date, request.Reason,
            request.Declaration, snapshot.Rows, snapshot.Changed, snapshot.Unchanged, snapshot.Rejected,
            token, token is null ? null : expiresAt);
    }

    public async Task<ProductFiscalBatchResult> ConfirmAsync(ProductFiscalBatchConfirmation confirmation)
    {
        if (!confirmation.Confirmed || confirmation.OperationId == Guid.Empty
            || string.IsNullOrWhiteSpace(confirmation.PreviewToken) || confirmation.PreviewToken.Length > 24576)
            throw Error("FISCAL_BATCH_CONFIRMATION_REQUIRED", 400);

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
                throw Error("FISCAL_BATCH_REQUEST_CONFLICT", 409);
            return ReadReceipt(previous);
        }

        PreviewProof? proof;
        try { proof = JsonSerializer.Deserialize<PreviewProof>(protector.Unprotect(confirmation.PreviewToken)); }
        catch (Exception e) when (e is CryptographicException or JsonException or ArgumentException)
        { throw Error("FISCAL_BATCH_PREVIEW_INVALID", 409); }
        if (proof is null || proof.OperationId != confirmation.OperationId || proof.CompanyId != actor.CompanyId
            || proof.UserId != actor.UserId || proof.EstablishmentId != actor.EstablishmentId
            || proof.EmissionPointId != actor.EmissionPointId)
            throw Error("FISCAL_BATCH_PREVIEW_INVALID", 409);

        var products = await costs.LockProductsAsync(actor.CompanyId, proof.Request.ProductIds);
        var now = clock.UtcNow;
        if (now >= proof.ExpiresAt) throw Error("FISCAL_BATCH_PREVIEW_EXPIRED", 409);
        var snapshot = await EvaluateAsync(actor, proof.Request, products, now);
        if (snapshot.Rejected != 0 || snapshot.Fingerprint != proof.Fingerprint)
            throw Error("FISCAL_BATCH_PREVIEW_CHANGED", 409);
        var result = new ProductFiscalBatchResult(proof.OperationId, actor.CompanyId, snapshot.CompanyName,
            actor.UserId, now, snapshot.Date, snapshot.ProfileRevision!.Value, proof.Request.Reason,
            snapshot.Rows, snapshot.Changed, snapshot.Unchanged, snapshot.Rejected);

        try
        {
            foreach (var declaration in snapshot.Changes)
            {
                var product = products[declaration.ProductId];
                product.VatCategory = declaration.DeclaredVatCategory;
                product.FiscalRevision = declaration.ProductFiscalRevision;
                db.ProductFiscalClassifications.Add(declaration);
            }
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

    public async Task<ProductFiscalBatchResult> ReceiptAsync(Guid operationId)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await RequirePermissionsAsync(actor);
        var batch = await FindReceiptAsync(actor.CompanyId, operationId);
        return batch is not null && OwnReceipt(batch, actor) ? ReadReceipt(batch)
            : throw Error("FISCAL_BATCH_RESULT_NOT_FOUND", 404);
    }

    private async Task<Evaluation> EvaluateAsync(OperationalContext actor, ProductFiscalBatchRequest request,
        IReadOnlyDictionary<int, Product> products, DateTime now)
    {
        var companyName = await db.Companies.Where(c => c.Id == actor.CompanyId).Select(c => c.Name).SingleAsync();
        var date = fiscalClock.GetEcuadorFiscalDate(now);
        CompanyFiscalProfile? profile = null;
        string? profileIssue = null;
        try { profile = await profiles.ResolveAsync(actor.CompanyId, date); }
        catch (InvalidOperationException e) when (FiscalProfileErrors.IsValidation(e.Message))
        { profileIssue = e.Message; }

        var classifications = await profiles.ResolveClassificationsAsync(actor.CompanyId, request.ProductIds);
        var categoryIds = products.Values.Select(p => p.CategoryId).Distinct().ToArray();
        var activeCategories = (await db.Categories.Where(c => c.CompanyId == actor.CompanyId
            && c.IsActive && categoryIds.Contains(c.Id)).Select(c => c.Id).ToArrayAsync()).ToHashSet();
        var withStock = (await db.ProductStocks.Where(s => s.CompanyId == actor.CompanyId
            && s.EstablishmentId == actor.EstablishmentId && request.ProductIds.Contains(s.ProductId)
            && s.Quantity > 0).Select(s => s.ProductId).ToArrayAsync()).ToHashSet();
        var nominal = FiscalTaxResolver.NominalCategory(request.Declaration.TaxBasis);
        var rows = new List<ProductFiscalBatchRow>(request.ProductIds.Length);
        var changes = new List<ProductFiscalClassification>();
        var productStates = new List<string>(products.Count);
        foreach (var id in request.ProductIds)
        {
            if (!products.TryGetValue(id, out var p))
            {
                rows.Add(new(id, null, null, null, null, nominal, null, null, null, null, "rejected", "PRODUCT_NOT_FOUND", []));
                continue;
            }
            classifications.TryGetValue(id, out var current);
            productStates.Add(ProductEditorService.Token(p, current));
            var warnings = new List<string>(3);
            if (!p.IsActive) warnings.Add("PRODUCT_INACTIVE");
            if (!activeCategories.Contains(p.CategoryId)) warnings.Add("CATEGORY_INACTIVE");
            if (p.TracksInventory && !withStock.Contains(id)) warnings.Add("INSUFFICIENT_STOCK");
            var currentMap = current is null ? null : ProductFiscalDeclaration.Map(current, p);
            var (currentDecision, currentIssue) = Resolve(p, current);
            ProductFiscalClassificationDto? proposedMap = null;
            FiscalDecision? proposedDecision = null;
            string? rejection = null;
            var outcome = "rejected";
            var proposedProduct = new Product
            {
                CompanyId = p.CompanyId, Id = p.Id, TracksInventory = p.TracksInventory,
                VatCategory = nominal ?? p.VatCategory,
                FiscalRevision = nominal.HasValue && nominal.Value != p.VatCategory
                    ? checked(p.FiscalRevision + 1) : p.FiscalRevision
            };
            try
            {
                var next = ProductFiscalDeclaration.Create(proposedProduct, request.Declaration, actor.UserId, now, current?.Revision ?? 0);
                proposedMap = ProductFiscalDeclaration.Map(next, proposedProduct);
                var unchanged = currentMap is { IsCurrent: true }
                    && currentMap == proposedMap with
                    {
                        Revision = currentMap.Revision, DeclaredByUserId = currentMap.DeclaredByUserId,
                        DeclaredAt = currentMap.DeclaredAt
                    } && p.VatCategory == proposedProduct.VatCategory;
                if (unchanged) proposedMap = currentMap;
                (proposedDecision, rejection) = Resolve(proposedProduct, unchanged ? current : next);
                if (rejection is null)
                {
                    outcome = unchanged ? "unchanged" : "changed";
                    if (!unchanged) changes.Add(next);
                }
            }
            catch (InvalidOperationException e) when (FiscalProfileErrors.IsValidation(e.Message))
            { rejection = e.Message; }

            rows.Add(new(id, p.Name, p.InternalCode, p.VatCategory, currentMap, nominal, proposedMap,
                currentDecision, currentIssue, proposedDecision, outcome, rejection, warnings.ToArray()));
        }

        var fingerprint = CriticalOperationRequest.Hash(new { companyName, date, ProfileId = profile?.Id, ProfileRevision = profile?.Revision,
            FiscalTaxResolver.RuleVersion, productStates,
            Rows = rows.Select(r => r with
            {
                // Declaration time is assigned on application, not part of reviewed fiscal facts.
                ProposedClassification = r.Outcome == "changed" ? r.ProposedClassification! with { DeclaredAt = default } : r.ProposedClassification
            }) });
        return new(companyName, date, profile?.Revision, rows.ToArray(), changes, changes.Count,
            rows.Count(r => r.Outcome == "unchanged"), rows.Count(r => r.Outcome == "rejected"), fingerprint);

        (FiscalDecision?, string?) Resolve(Product product, ProductFiscalClassification? classification)
        {
            if (profile is null) return (null, profileIssue);
            try
            {
                ProductInventoryPolicy.ValidateClassification(product, classification);
                return (FiscalTaxResolver.Resolve(profile, product, classification, date), null);
            }
            catch (InvalidOperationException e) when (FiscalProfileErrors.IsValidation(e.Message)) { return (null, e.Message); }
        }
    }

    private async Task RequirePermissionsAsync(OperationalContext actor)
    {
        try { await ElectronicIssuingCoordinator.RequirePermissionsAsync(db, actor.UserId, actor.CompanyId, Permissions); }
        catch (InvalidOperationException e) when (e.Message == "FISCAL_PERMISSION_REQUIRED") { throw Error(e.Message, 403); }
    }

    private Task<InitialDataBatch?> FindReceiptAsync(int companyId, Guid id) =>
        db.InitialDataBatches.AsNoTracking().SingleOrDefaultAsync(b => b.CompanyId == companyId && b.RequestId == id);

    private static bool OwnReceipt(InitialDataBatch b, OperationalContext a) => b.Kind == Kind
        && b.UserId == a.UserId && b.EstablishmentId == a.EstablishmentId && b.EmissionPointId == a.EmissionPointId;

    private static ProductFiscalBatchResult ReadReceipt(InitialDataBatch b) =>
        JsonSerializer.Deserialize<ProductFiscalBatchResult>(b.ResultJson)!;

    private static ProductFiscalBatchRequest Normalize(ProductFiscalBatchRequest request)
    {
        if (request.ProductIds is not { Length: > 0 and <= MaxProducts } ids
            || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
            throw Error("FISCAL_BATCH_SELECTION_INVALID", 400);
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500
            || request.Declaration is null || request.Declaration.LegalReference?.Length > 500
            || request.Declaration.EvidenceReference?.Length > 500 || request.Declaration.SriMaterialCode?.Length > 25)
            throw Error("FISCAL_BATCH_DECLARATION_INVALID", 400);
        return request with { ProductIds = ids.Order().ToArray(), Reason = request.Reason.Trim() };
    }

    private static OperationalContextException Error(string code, int status) => new(code, status);

    private sealed record PreviewProof(Guid OperationId, int CompanyId, int UserId, int EstablishmentId,
        int EmissionPointId, DateTime ExpiresAt, ProductFiscalBatchRequest Request, string Fingerprint);

    private sealed record Evaluation(string CompanyName, DateOnly Date, long? ProfileRevision,
        ProductFiscalBatchRow[] Rows, List<ProductFiscalClassification> Changes, int Changed, int Unchanged,
        int Rejected, string Fingerprint);
}
