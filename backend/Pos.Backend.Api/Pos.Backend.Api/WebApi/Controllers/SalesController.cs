using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public class SalesController : ControllerBase
{
    private readonly ISalesService _salesService;

    public SalesController(ISalesService salesService)
    {
        _salesService = salesService;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SaleListItemDto>>> Get([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] SaleStatus? status, [FromQuery] string? search, [FromQuery] int? userId)
    {
        var sales = await _salesService.GetSalesAsync(from, to, status, search, userId);
        return Ok(sales);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> GetById(int id)
    {
        var sale = await _salesService.GetByIdAsync(id);
        if (sale is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SALE_NOT_FOUND" });
        }

        return Ok(sale);
    }

    [HttpGet("{id:int}/sri/xml-draft")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> GetSriXmlDraft(int id)
    {
        var xmlDraft = await _salesService.GetSriXmlDraftAsync(id);

        if (xmlDraft is null)
        {
            return NotFound(new ApiErrorResponse { Error = "SRI_XML_DRAFT_NOT_FOUND" });
        }

        return Content(xmlDraft, "application/xml");
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.PosSalesCreate)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> Create([FromBody] SaleCreateDto dto)
    {
        try
        {
            var sale = await _salesService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = sale.Id }, sale);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/void")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SaleDto>> Void(int id, [FromBody] VoidSaleDto dto)
    {
        try
        {
            var sale = await _salesService.VoidAsync(id, dto);
            return Ok(sale);
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
            "SALE_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "PRODUCT_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CUSTOMER_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "SALE_ITEMS_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            "PRODUCT_INACTIVE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_QUANTITY" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_UNIT_PRICE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_LINE_DISCOUNT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_DISCOUNT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_PRODUCT_VAT_CATEGORY" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_PAYMENT_METHOD" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SALE_DOCUMENT_TYPE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_DOCUMENT_TYPE" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_ISSUER_RUC" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_DOCUMENT_CONTEXT" => BadRequest(new ApiErrorResponse { Error = code }),
            "INVALID_SRI_CUSTOMER_IDENTIFICATION" => BadRequest(new ApiErrorResponse { Error = code }),
            "SRI_ACCESS_KEY_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SRI_XML_DRAFT_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "DOCUMENT_SEQUENCE_ERROR" => Conflict(new ApiErrorResponse { Error = code }),
            "DOCUMENT_NUMBER_GENERATION_FAILED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_ALREADY_VOIDED" => Conflict(new ApiErrorResponse { Error = code }),
            "SALE_NOT_VOIDABLE" => Conflict(new ApiErrorResponse { Error = code }),
            "INSUFFICIENT_STOCK" => Conflict(new ApiErrorResponse { Error = code }),
            "INVENTORY_CONCURRENCY_CONFLICT" => Conflict(new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "SALE_OPERATION_FAILED" })
        };
    }
}
