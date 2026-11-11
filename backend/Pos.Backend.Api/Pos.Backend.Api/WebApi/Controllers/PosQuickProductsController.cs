using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppPermissions.InventoryRead)]
[Authorize(Policy = AppPermissions.CatalogProductsRead)]
[Authorize(Policy = AppPermissions.PosSalesCreate)]
[RequireOperationalContext]
public sealed class PosQuickProductsController(IPosQuickProductService service) : ControllerBase
{
    [HttpGet("favorites")]
    public async Task<ActionResult<PosFavoritesDto>> GetFavorites(CancellationToken cancellationToken)
        => Ok(await service.GetFavoritesAsync(cancellationToken));

    [HttpPut("favorites")]
    [Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    public async Task<ActionResult<PosFavoritesDto>> SaveFavorites(
        SavePosFavoritesDto dto, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await service.SaveFavoritesAsync(dto, cancellationToken));
        }
        catch (InvalidOperationException ex) when (ex.Message == "POS_FAVORITES_STALE")
        {
            return Conflict(new ApiErrorResponse { Error = ex.Message });
        }
        catch (InvalidOperationException ex) when (ex.Message is "POS_FAVORITES_INVALID" or "POS_FAVORITES_PRODUCT_INVALID")
        {
            return BadRequest(new ApiErrorResponse { Error = ex.Message });
        }
    }

    [HttpGet("top-sellers")]
    public async Task<ActionResult<int[]>> GetTopSellers(CancellationToken cancellationToken)
        => Ok(await service.GetTopSellersAsync(cancellationToken));
}
