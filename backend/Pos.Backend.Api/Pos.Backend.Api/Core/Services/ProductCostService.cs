using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Core.Services;

public sealed class ProductCostService(PosDbContext context) : IProductCostService
{
    public async Task<IReadOnlyDictionary<int, Product>> LockProductsAsync(
        int companyId,
        IEnumerable<int> productIds)
    {
        EnsureTransaction();

        var ids = productIds.Distinct().OrderBy(id => id).ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<int, Product>();
        }

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1
            FROM "Products"
            WHERE "CompanyId" = {companyId}
              AND "Id" = ANY({ids})
            ORDER BY "Id"
            FOR UPDATE
            """);

        return await context.Products
            .Where(product => product.CompanyId == companyId && ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id);
    }

    public void InitializeManualCost(Product product, int userId, DateTime createdAt)
    {
        EnsureTransaction();

        if (product.Id <= 0 || product.CurrentCostEventId.HasValue)
        {
            throw new ProductCostIntegrityException();
        }

        var costEvent = CreateEvent(
            product,
            revision: 0,
            previousCost: product.Cost,
            cost: product.Cost,
            ProductCostSourceType.Manual,
            purchaseReceiptItem: null,
            userId,
            createdAt);

        product.LastCostRevision = 0;
        product.CurrentCostEvent = costEvent;
    }

    public void ApplyManualCost(Product product, decimal cost, int userId, DateTime createdAt)
    {
        EnsureTransaction();
        StageCostEvent(product, cost, ProductCostSourceType.Manual, null, userId, createdAt);
    }

    public void ApplyPurchaseReceiptCost(
        Product product,
        PurchaseReceiptItem item,
        int userId,
        DateTime createdAt)
    {
        EnsureTransaction();

        if (item.ProductId != product.Id)
        {
            throw new ProductCostIntegrityException();
        }

        item.PreviousProductCost = product.Cost;
        item.AppliedProductCost = item.UnitCost;
        StageCostEvent(
            product,
            item.UnitCost,
            ProductCostSourceType.PurchaseReceipt,
            item,
            userId,
            createdAt);
    }

    public async Task ResolveCancellationAsync(
        int companyId,
        PurchaseReceipt receipt,
        Product product,
        IReadOnlyCollection<PurchaseReceiptItem> productItems)
    {
        EnsureTransaction();

        if (receipt.CompanyId != companyId || product.CompanyId != companyId || productItems.Count == 0
            || productItems.Any(item => item.ProductId != product.Id || item.PurchaseReceiptId != receipt.Id))
        {
            throw new ProductCostIntegrityException();
        }

        var currentEvent = await LoadEventAsync(product.CurrentCostEventId, companyId, product.Id)
            ?? throw new ProductCostIntegrityException();

        if (currentEvent.SourceType == ProductCostSourceType.PurchaseReceipt
            && (!currentEvent.PurchaseReceiptId.HasValue || !currentEvent.PurchaseReceiptStatus.HasValue))
        {
            throw new ProductCostIntegrityException();
        }

        if (currentEvent.SourceType == ProductCostSourceType.PurchaseReceipt
            && currentEvent.PurchaseReceiptId != receipt.Id
            && currentEvent.PurchaseReceiptStatus == PurchaseReceiptStatus.Canceled)
        {
            throw new ProductCostIntegrityException();
        }

        var costChanged = currentEvent.SourceType == ProductCostSourceType.PurchaseReceipt
            && currentEvent.PurchaseReceiptId == receipt.Id;

        if (costChanged)
        {
            // Never revive another line from this receipt or an already canceled receipt.
            var replacement = await context.ProductCostEvents
                .AsNoTracking()
                .Where(costEvent => costEvent.CompanyId == companyId
                    && costEvent.ProductId == product.Id
                    && costEvent.Revision < currentEvent.Revision
                    && (costEvent.SourceType != ProductCostSourceType.PurchaseReceipt
                        || (costEvent.PurchaseReceiptItem != null
                            && costEvent.PurchaseReceiptItem.PurchaseReceiptId != receipt.Id
                            && costEvent.PurchaseReceiptItem.PurchaseReceipt.Status == PurchaseReceiptStatus.Posted)))
                .OrderByDescending(costEvent => costEvent.Revision)
                .Select(costEvent => new { costEvent.Id, costEvent.Cost })
                .FirstOrDefaultAsync()
                ?? throw new ProductCostIntegrityException();

            product.Cost = replacement.Cost;
            product.CurrentCostEventId = replacement.Id;
            product.CurrentCostEvent = null;
        }

        foreach (var item in productItems)
        {
            item.ProductCostChangedOnCancellation = costChanged;
            item.ProductCostAfterCancellation = product.Cost;
        }
    }

    private void StageCostEvent(
        Product product,
        decimal cost,
        ProductCostSourceType sourceType,
        PurchaseReceiptItem? purchaseReceiptItem,
        int userId,
        DateTime createdAt)
    {
        if (product.Id <= 0 || !product.CurrentCostEventId.HasValue)
        {
            throw new ProductCostIntegrityException();
        }

        var revision = checked(product.LastCostRevision + 1);
        var costEvent = CreateEvent(
            product,
            revision,
            product.Cost,
            cost,
            sourceType,
            purchaseReceiptItem,
            userId,
            createdAt);

        product.Cost = cost;
        product.LastCostRevision = revision;
        product.CurrentCostEvent = costEvent;
    }

    private ProductCostEvent CreateEvent(
        Product product,
        long revision,
        decimal previousCost,
        decimal cost,
        ProductCostSourceType sourceType,
        PurchaseReceiptItem? purchaseReceiptItem,
        int userId,
        DateTime createdAt)
    {
        if ((sourceType == ProductCostSourceType.PurchaseReceipt) != (purchaseReceiptItem is not null))
        {
            throw new ProductCostIntegrityException();
        }

        var costEvent = new ProductCostEvent
        {
            CompanyId = product.CompanyId,
            ProductId = product.Id,
            Revision = revision,
            PreviousCost = previousCost,
            Cost = cost,
            SourceType = sourceType,
            PurchaseReceiptItem = purchaseReceiptItem,
            CreatedAt = createdAt,
            CreatedByUserId = userId
        };

        context.ProductCostEvents.Add(costEvent);
        return costEvent;
    }

    private async Task<CostEventSnapshot?> LoadEventAsync(int? id, int companyId, int productId)
    {
        if (!id.HasValue)
        {
            return null;
        }

        return await context.ProductCostEvents
            .AsNoTracking()
            .Where(costEvent => costEvent.Id == id.Value
                && costEvent.CompanyId == companyId
                && costEvent.ProductId == productId)
            .Select(costEvent => new CostEventSnapshot(
                costEvent.Revision,
                costEvent.SourceType,
                costEvent.PurchaseReceiptItemId != null
                    ? costEvent.PurchaseReceiptItem!.PurchaseReceiptId
                    : null,
                costEvent.PurchaseReceiptItemId != null
                    ? costEvent.PurchaseReceiptItem!.PurchaseReceipt.Status
                    : null))
            .SingleOrDefaultAsync();
    }

    private void EnsureTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Product cost changes require a transaction.");
        }
    }

    private sealed record CostEventSnapshot(
        long Revision,
        ProductCostSourceType SourceType,
        int? PurchaseReceiptId,
        PurchaseReceiptStatus? PurchaseReceiptStatus);
}
