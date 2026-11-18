using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController, RequireOperationalContext]
[Authorize(Policy = AppPermissions.CatalogProductsRead)]
[Authorize(Policy = AppPermissions.CatalogProductPricesBatch)]
[Route("api/products/price-batches")]
public sealed class ProductPriceBatchesController(ProductPriceBatchService service) : ControllerBase
{
    [HttpGet("candidates")]
    public Task<ProductFiscalBatchCandidates> Candidates(string? search, bool? active,
        int? categoryId, string? fiscalStatus, ProductVatCategory? vatCategory, CancellationToken cancellationToken)
        => service.CandidatesAsync(search, active, categoryId, fiscalStatus, vatCategory, cancellationToken);

    [HttpPost("preview"), RequestSizeLimit(32768)]
    public Task<ProductPriceBatchPreview> Preview(ProductPriceBatchRequest request) => service.PreviewAsync(request);

    [HttpPost("confirm"), RequestSizeLimit(32768)]
    public Task<ProductPriceBatchResult> Confirm(ProductPriceBatchConfirmation confirmation) => service.ConfirmAsync(confirmation);

    [HttpGet("{operationId:guid}")]
    public Task<ProductPriceBatchResult> Receipt(Guid operationId) => service.ReceiptAsync(operationId);

    [HttpGet("recent")]
    public Task<ProductPriceBatchReceiptSummary[]> Recent(CancellationToken cancellationToken)
        => service.RecentReceiptsAsync(cancellationToken);
}
