using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController, Authorize, RequireOperationalContext]
[Route("api/FiscalSettings/profiles")]
public sealed class FiscalProfilesController(IFiscalProfileService service) : ControllerBase
{
    [HttpGet, Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult> History() => Ok(await service.GetHistoryAsync());

    [HttpPost, Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult> Declare(DeclareFiscalProfileDto dto)
    {
        try { return Ok(await service.DeclareAsync(dto)); }
        catch (InvalidOperationException ex) when (FiscalProfileErrors.IsValidation(ex.Message))
        { return Conflict(new ApiErrorResponse { Error = ex.Message }); }
    }

    [HttpGet("products/{productId:int}"), Authorize(Policy = AppPermissions.FiscalSettingsRead)]
    public async Task<ActionResult> Product(int productId)
    {
        try { return Ok(await service.GetProductClassificationAsync(productId)); }
        catch (KeyNotFoundException) { return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" }); }
    }

    [HttpPost("products/{productId:int}"), Authorize(Policy = AppPermissions.FiscalSettingsWrite)]
    public async Task<ActionResult> DeclareProduct(int productId, DeclareProductFiscalClassificationDto dto)
    {
        try { return Ok(await service.DeclareProductClassificationAsync(productId, dto)); }
        catch (KeyNotFoundException) { return NotFound(new ApiErrorResponse { Error = "PRODUCT_NOT_FOUND" }); }
        catch (InvalidOperationException ex) when (FiscalProfileErrors.IsValidation(ex.Message))
        { return Conflict(new ApiErrorResponse { Error = ex.Message }); }
    }
}
