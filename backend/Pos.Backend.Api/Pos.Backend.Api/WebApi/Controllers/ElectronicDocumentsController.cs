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
public sealed class ElectronicDocumentsController(
    IElectronicDocumentQueryService electronicDocumentQueryService)
    : ControllerBase
{
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
