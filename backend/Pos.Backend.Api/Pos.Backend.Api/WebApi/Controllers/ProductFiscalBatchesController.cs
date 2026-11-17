using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController, RequireOperationalContext]
[Authorize(Policy = AppPermissions.CatalogProductsRead)]
[Authorize(Policy = AppPermissions.CatalogProductsWrite)]
[Authorize(Policy = AppPermissions.FiscalSettingsRead)]
[Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
[Route("api/products/fiscal-batches")]
public sealed class ProductFiscalBatchesController(ProductFiscalBatchService service) : ControllerBase
{
    [HttpGet("candidates")]
    public Task<PagedResultDto<ProductFiscalBatchCandidate>> Candidates(string? search, bool? active,
        int? categoryId, string? fiscalStatus, int page = 1, int pageSize = 30)
        => service.PageAsync(search, active, categoryId, fiscalStatus, page, pageSize);

    [HttpPost("preview"), RequestSizeLimit(32768)]
    public Task<ProductFiscalBatchPreview> Preview(ProductFiscalBatchRequest request) => service.PreviewAsync(request);

    [HttpPost("confirm"), RequestSizeLimit(32768)]
    public Task<ProductFiscalBatchResult> Confirm(ProductFiscalBatchConfirmation confirmation) => service.ConfirmAsync(confirmation);

    [HttpGet("{operationId:guid}")]
    public Task<ProductFiscalBatchResult> Receipt(Guid operationId) => service.ReceiptAsync(operationId);
}
