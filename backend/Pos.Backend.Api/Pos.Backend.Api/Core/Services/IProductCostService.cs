using Pos.Backend.Api.Core.Entities;

namespace Pos.Backend.Api.Core.Services;

public interface IProductCostService
{
    Task<IReadOnlyDictionary<int, Product>> LockProductsAsync(int companyId, IEnumerable<int> productIds);
    void InitializeManualCost(Product product, int userId, DateTime createdAt);
    void ApplyManualCost(Product product, decimal cost, int userId, DateTime createdAt);
    void ApplyPurchaseReceiptCost(Product product, PurchaseReceiptItem item, int userId, DateTime createdAt);
    Task ResolveCancellationAsync(
        int companyId,
        PurchaseReceipt receipt,
        Product product,
        IReadOnlyCollection<PurchaseReceiptItem> productItems);
}
