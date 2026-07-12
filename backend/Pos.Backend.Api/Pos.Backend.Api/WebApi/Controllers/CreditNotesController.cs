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
public class CreditNotesController : ControllerBase
{
    private readonly ICreditNoteService _creditNoteService;

    public CreditNotesController(ICreditNoteService creditNoteService)
    {
        _creditNoteService = creditNoteService;
    }

    [HttpGet("original-sales/{saleId:int}/eligibility")]
    [Authorize(Policy = AppPermissions.PosSalesVoid)]
    [ProducesResponseType(typeof(CreditNoteEligibilityDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreditNoteEligibilityDto>> GetEligibility(int saleId)
    {
        try
        {
            return Ok(await _creditNoteService.GetEligibilityAsync(saleId));
        }
        catch (KeyNotFoundException ex) when (ex.Message == "SALE_NOT_FOUND")
        {
            return NotFound(new ApiErrorResponse { Error = "SALE_NOT_FOUND" });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new ApiErrorResponse { Error = "CREDIT_NOTE_OPERATION_FAILED" });
        }
    }
}
