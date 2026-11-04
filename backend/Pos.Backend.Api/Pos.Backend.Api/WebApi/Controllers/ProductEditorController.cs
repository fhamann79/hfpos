using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController, Authorize, RequireOperationalContext]
[Route("api/Products/editor")]
public sealed class ProductEditorController(IProductEditorService service) : ControllerBase
{
    [HttpGet("{id:int}"), Authorize(Policy = AppPermissions.CatalogProductsRead)]
    public async Task<ActionResult<ProductEditorStateDto>> Get(int id) => Ok(await service.GetAsync(id));

    [HttpPost, Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    public async Task<ActionResult<ProductEditorStateDto>> Create(ProductEditorSaveDto dto)
    {
        var result = await service.SaveAsync(null, dto);
        return CreatedAtAction(nameof(Get), new { id = result.Product.Id }, result);
    }

    [HttpPut("{id:int}"), Authorize(Policy = AppPermissions.CatalogProductsWrite)]
    public async Task<ActionResult<ProductEditorStateDto>> Update(int id, ProductEditorSaveDto dto)
        => Ok(await service.SaveAsync(id, dto));
}
