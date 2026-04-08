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
[Authorize]
[RequireOperationalContext]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet("stocks")]
    [Authorize(Policy = AppPermissions.InventoryRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<InventoryStockListItemDto>>> GetStocks([FromQuery] string? search, [FromQuery] int? productId, [FromQuery] bool onlyPositive = false)
    {
        var stocks = await _inventoryService.GetStocksAsync(search, productId, onlyPositive);
        return Ok(stocks);
    }

    [HttpGet("products/{productId:int}/stock")]
    [Authorize(Policy = AppPermissions.InventoryRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryStockDto>> GetProductStock(int productId)
    {
        try
        {
            var stock = await _inventoryService.GetProductStockAsync(productId);
            return Ok(stock);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpGet("products/{productId:int}/movements")]
    [Authorize(Policy = AppPermissions.InventoryRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<InventoryMovementDto>>> GetProductMovements(int productId)
    {
        try
        {
            var movements = await _inventoryService.GetProductMovementsAsync(productId);
            return Ok(movements);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("entry")]
    [Authorize(Policy = AppPermissions.InventoryWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryMovementDto>> RegisterEntry([FromBody] InventoryEntryDto dto)
    {
        try
        {
            var result = await _inventoryService.RegisterEntryAsync(dto);
            return Ok(result);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("exit")]
    [Authorize(Policy = AppPermissions.InventoryWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryMovementDto>> RegisterExit([FromBody] InventoryExitDto dto)
    {
        try
        {
            var result = await _inventoryService.RegisterExitAsync(dto);
            return Ok(result);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("adjust")]
    [Authorize(Policy = AppPermissions.InventoryWrite)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InventoryMovementDto>> RegisterAdjustment([FromBody] InventoryAdjustDto dto)
    {
        try
        {
            var result = await _inventoryService.RegisterAdjustmentAsync(dto);
            return Ok(result);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    private ActionResult MapDomainError(Exception exception)
    {
        var code = exception.Message;

        return code switch
        {
            "PRODUCT_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "PRODUCT_INACTIVE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_QUANTITY" => BadRequest(new ApiErrorResponse { Error = code }),
            "INSUFFICIENT_STOCK" => Conflict(new ApiErrorResponse { Error = code }),
            "INVENTORY_CONCURRENCY_CONFLICT" => Conflict(new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "INVENTORY_OPERATION_FAILED" })
        };
    }
}
