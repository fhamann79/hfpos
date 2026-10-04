using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Enums;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireOperationalContext]
public sealed class ElectronicDocumentsController(
    IElectronicDocumentQueryService electronicDocumentQueryService,
    IElectronicIssuingRecoveryService issuing,
    IOperationalContextAccessor contextAccessor)
    : ControllerBase
{
    [HttpPost("invoices/{id:int}/resume")]
    [Authorize(Policy = AppPermissions.SriDocumentsSubmit)]
    public async Task<IActionResult> Resume(int id, CancellationToken cancellationToken)
    {
        try
        {
            await issuing.ResumeAsync(id, await contextAccessor.GetRequiredContextAsync(), cancellationToken);
            return Accepted();
        }
        catch (KeyNotFoundException ex) { return NotFound(new ApiErrorResponse { Error = ex.Message }); }
        catch (InvalidOperationException ex)
        {
            if (ex.Message == "FISCAL_PERMISSION_REQUIRED") return StatusCode(403, new ApiErrorResponse { Error = ex.Message });
            return Conflict(new ApiErrorResponse { Error = ElectronicIssuingCoordinator.SafeCode(ex.Message) });
        }
    }
    [HttpGet]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(typeof(ElectronicDocumentListResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ElectronicDocumentListResultDto>> Get(
        [FromQuery] ElectronicDocumentQueryDto query)
        => Ok(await electronicDocumentQueryService.GetListAsync(query));

    [HttpGet("{kind}/{id:int}")]
    [Authorize(Policy = AppPermissions.ReportsSalesRead)]
    [ProducesResponseType(typeof(ElectronicDocumentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ElectronicDocumentDetailDto>> GetById(
        ElectronicDocumentKind kind,
        int id)
    {
        var document = await electronicDocumentQueryService.GetByIdAsync(kind, id);

        return document is null
            ? NotFound(new ApiErrorResponse { Error = "ELECTRONIC_DOCUMENT_NOT_FOUND" })
            : Ok(document);
    }
}
