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
public class CashSessionsController : ControllerBase
{
    private readonly ICashSessionService _cashSessionService;

    public CashSessionsController(ICashSessionService cashSessionService)
    {
        _cashSessionService = cashSessionService;
    }

    [HttpGet("current")]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CashSessionDto?>> GetCurrent()
    {
        return Ok(await _cashSessionService.GetCurrentAsync());
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CashSessionListItemDto>>> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] CashSessionStatus? status,
        [FromQuery] int? userId)
    {
        return Ok(await _cashSessionService.GetListAsync(from, to, status, userId));
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.CashSessionsRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CashSessionDto>> GetById(int id)
    {
        var session = await _cashSessionService.GetByIdAsync(id);
        if (session is null)
        {
            return NotFound(new ApiErrorResponse { Error = "CASH_SESSION_NOT_FOUND" });
        }

        return Ok(session);
    }

    [HttpPost("open")]
    [Authorize(Policy = AppPermissions.CashSessionsWrite)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashSessionDto>> Open([FromBody] OpenCashSessionDto dto)
    {
        try
        {
            var session = await _cashSessionService.OpenAsync(dto ?? new OpenCashSessionDto());
            return CreatedAtAction(nameof(GetById), new { id = session.Id }, session);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/movements")]
    [Authorize(Policy = AppPermissions.CashSessionsWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashSessionDto>> AddMovement(int id, [FromBody] CreateCashMovementDto dto)
    {
        try
        {
            return Ok(await _cashSessionService.AddMovementAsync(id, dto ?? new CreateCashMovementDto()));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            return MapDomainError(ex);
        }
    }

    [HttpPost("{id:int}/close")]
    [Authorize(Policy = AppPermissions.CashSessionsWrite)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CashSessionDto>> Close(int id, [FromBody] CloseCashSessionDto dto)
    {
        try
        {
            return Ok(await _cashSessionService.CloseAsync(id, dto ?? new CloseCashSessionDto()));
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
            "CASH_SESSION_NOT_FOUND" => NotFound(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_CONTEXT_MISMATCH" => StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse { Error = code }),
            "CASH_SESSION_ALREADY_OPEN" => Conflict(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_NOT_OPEN" => Conflict(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_ALREADY_CLOSED" => Conflict(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_OPENING_AMOUNT_INVALID" => BadRequest(new ApiErrorResponse { Error = code }),
            "CASH_SESSION_COUNTED_AMOUNT_INVALID" => BadRequest(new ApiErrorResponse { Error = code }),
            "CASH_MOVEMENT_AMOUNT_INVALID" => BadRequest(new ApiErrorResponse { Error = code }),
            "CASH_MOVEMENT_REASON_REQUIRED" => BadRequest(new ApiErrorResponse { Error = code }),
            _ => BadRequest(new ApiErrorResponse { Error = "CASH_SESSION_OPERATION_FAILED" })
        };
    }
}
