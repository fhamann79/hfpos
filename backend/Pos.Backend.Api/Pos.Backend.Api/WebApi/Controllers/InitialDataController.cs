using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController, Route("api/initial-data"), Authorize, RequireOperationalContext]
public sealed class InitialDataController(IInitialDataService service) : ControllerBase
{
    [HttpGet("templates/{kind}")]
    public Task<IActionResult> Template(string kind) => Execute(async () =>
        File(Encoding.UTF8.GetBytes(await service.GetTemplateAsync(kind)), "text/csv; charset=utf-8", $"{kind}.csv"));
    [HttpPost("preview"), RequestSizeLimit(2200000)]
    public Task<IActionResult> Preview(InitialDataPreviewRequest request) => Execute(async () => Ok(await service.PreviewAsync(request)));
    [HttpPost("confirm"), RequestSizeLimit(2200000)]
    public Task<IActionResult> Confirm(InitialDataConfirmRequest request) => Execute(async () => Ok(await service.ConfirmAsync(request)));
    [HttpGet("batches")]
    public Task<IActionResult> Batches([FromQuery] int page = 1) => Execute(async () => Ok(await service.GetBatchesAsync(page)));
    [HttpGet("readiness")]
    public Task<IActionResult> Readiness() => Execute(async () => Ok(await service.GetReadinessAsync()));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("INITIAL_DATA_", StringComparison.Ordinal))
        {
            var response = new ApiErrorResponse { Error = ex.Message };
            return ex.Message is "INITIAL_DATA_REQUEST_CONFLICT" or "INITIAL_DATA_REVALIDATION_FAILED"
                ? Conflict(response) : BadRequest(response);
        }
    }
}
