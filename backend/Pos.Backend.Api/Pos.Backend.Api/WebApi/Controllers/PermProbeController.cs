using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.Security;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/perm-probe")]
public class PermProbeController : ControllerBase
{
    [HttpGet("products-write")]
    [Authorize(Policy = $"Perm:{AppPermissions.CatalogProductsWrite}")]
    public IActionResult ProductsWrite()
    {
        return Ok("OK_PRODUCTS_WRITE");
    }

    [HttpGet("reports-read")]
    [Authorize(Policy = $"Perm:{AppPermissions.ReportsSalesRead}")]
    public IActionResult ReportsRead()
    {
        return Ok("OK_REPORTS_READ");
    }
}
