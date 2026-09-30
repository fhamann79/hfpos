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
public sealed class PaymentSettlementsController(IPaymentSettlementService service) : ControllerBase
{
    [HttpGet("reconciliation")]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    public async Task<ActionResult<PaymentReconciliationDto>> GetReconciliation([FromQuery] DateOnly? businessDate)
    {
        try { return Ok(await service.GetReconciliationAsync(businessDate)); }
        catch (InvalidOperationException ex) when (ex.Message == "PAYMENT_SETTLEMENT_DATE_INVALID")
        { return BadRequest(new ApiErrorResponse { Error = ex.Message }); }
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    public async Task<ActionResult<PagedResultDto<PaymentSettlementDto>>> Get([FromQuery] PaymentSettlementQueryDto query) =>
        Ok(await service.GetAsync(query));

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    public async Task<ActionResult<PaymentSettlementDto>> GetById(int id)
    {
        var settlement = await service.GetByIdAsync(id);
        return settlement is null
            ? NotFound(new ApiErrorResponse { Error = "PAYMENT_SETTLEMENT_NOT_FOUND" })
            : Ok(settlement);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.CashSessionsWrite)]
    public async Task<ActionResult<PaymentSettlementDto>> Create([FromBody] PaymentSettlementCreateDto request)
    {
        try { return Ok(await service.CreateAsync(request)); }
        catch (InvalidOperationException ex)
        {
            var response = new ApiErrorResponse { Error = ex.Message };
            return ex.Message switch
            {
                "PAYMENT_SETTLEMENT_REQUEST_CONFLICT" or "PAYMENT_SETTLEMENT_ALREADY_RECONCILED" => Conflict(response),
                "PAYMENT_SETTLEMENT_REQUEST_ID_REQUIRED" or "PAYMENT_SETTLEMENT_DATE_INVALID"
                    or "PAYMENT_SETTLEMENT_DATE_NOT_FINAL" or "PAYMENT_SETTLEMENT_METHOD_INVALID"
                    or "PAYMENT_SETTLEMENT_AMOUNT_INVALID" or "PAYMENT_SETTLEMENT_TEXT_INVALID" => BadRequest(response),
                _ => StatusCode(StatusCodes.Status500InternalServerError,
                    new ApiErrorResponse { Error = "PAYMENT_SETTLEMENT_OPERATION_FAILED" })
            };
        }
    }
}
