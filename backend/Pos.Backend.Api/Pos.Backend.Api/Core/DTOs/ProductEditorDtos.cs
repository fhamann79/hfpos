namespace Pos.Backend.Api.Core.DTOs;

public sealed class ProductEditorSaveDto : ProductCreateDto
{
    public string? ExpectedStateToken { get; set; }
    public bool SetManualCost { get; set; }
    public DeclareProductFiscalClassificationDto? Classification { get; set; }
}

public sealed record ProductEditorStateDto(ProductDto Product, string StateToken, long ProductRevision,
    long ClassificationRevision, ProductFiscalClassificationDto? Classification, bool CanDisableInventory,
    IReadOnlyList<string> SalePending);
