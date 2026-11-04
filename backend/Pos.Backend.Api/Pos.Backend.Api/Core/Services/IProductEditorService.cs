using Pos.Backend.Api.Core.DTOs;

namespace Pos.Backend.Api.Core.Services;

public interface IProductEditorService
{
    Task<ProductEditorStateDto> GetAsync(int id);
    Task<ProductEditorStateDto> SaveAsync(int? id, ProductEditorSaveDto dto);
}
