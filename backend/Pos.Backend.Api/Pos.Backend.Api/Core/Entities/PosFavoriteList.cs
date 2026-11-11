namespace Pos.Backend.Api.Core.Entities;

public sealed class PosFavoriteList
{
    public int CompanyId { get; set; }
    public long Revision { get; set; }
    public int[] ProductIds { get; set; } = [];
}
