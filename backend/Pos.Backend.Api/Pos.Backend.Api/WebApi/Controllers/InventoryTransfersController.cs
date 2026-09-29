using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/Inventory/transfers")]
[Authorize]
[RequireOperationalContext]
public sealed class InventoryTransfersController(IInventoryTransferService service) : ControllerBase
{
    [HttpGet("destinations")]
    [Authorize(Policy = AppPermissions.InventoryWrite)]
    public async Task<ActionResult<IReadOnlyList<InventoryTransferDestinationDto>>> GetDestinations() =>
        Ok(await service.GetDestinationsAsync());

    [HttpGet]
    [Authorize(Policy = AppPermissions.InventoryRead)]
    public async Task<ActionResult<PagedResultDto<InventoryTransferListItemDto>>> Get(
        [FromQuery] InventoryTransferQueryDto query) =>
        Ok(await service.GetAsync(query));

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.InventoryRead)]
    public async Task<ActionResult<InventoryTransferDetailDto>> GetById(int id)
    {
        var transfer = await service.GetByIdAsync(id);
        return transfer is null
            ? NotFound(new ApiErrorResponse { Error = "INVENTORY_TRANSFER_NOT_FOUND" })
            : Ok(transfer);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.InventoryWrite)]
    public async Task<ActionResult<InventoryTransferDetailDto>> Create(
        [FromBody] InventoryTransferCreateDto request)
    {
        try
        {
            return Ok(await service.CreateAsync(request));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            var code = ex.Message;
            var response = new ApiErrorResponse { Error = code };
            return code switch
            {
                "INVENTORY_TRANSFER_DESTINATION_NOT_FOUND" or "PRODUCT_NOT_FOUND" =>
                    NotFound(response),
                "INVENTORY_TRANSFER_INSUFFICIENT_STOCK"
                    or "INVENTORY_TRANSFER_CONCURRENCY_CONFLICT"
                    or "INVENTORY_TRANSFER_REQUEST_CONFLICT" => Conflict(response),
                "INVENTORY_TRANSFER_REQUEST_ID_REQUIRED"
                    or "INVENTORY_TRANSFER_DESTINATION_REQUIRED"
                    or "INVENTORY_TRANSFER_DESTINATION_INACTIVE"
                    or "INVENTORY_TRANSFER_SAME_ESTABLISHMENT"
                    or "INVENTORY_TRANSFER_ITEMS_REQUIRED"
                    or "INVENTORY_TRANSFER_ITEMS_INVALID"
                    or "INVENTORY_TRANSFER_QUANTITY_INVALID"
                    or "INVENTORY_TRANSFER_DUPLICATE_PRODUCT"
                    or "INVENTORY_TRANSFER_REFERENCE_INVALID"
                    or "INVENTORY_TRANSFER_NOTES_INVALID" => BadRequest(response),
                _ => BadRequest(new ApiErrorResponse { Error = "INVENTORY_TRANSFER_OPERATION_FAILED" })
            };
        }
    }
}
