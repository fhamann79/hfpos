namespace Pos.Backend.Api.Core.DTOs;

public sealed record PosFavoritesDto(long Revision, int[] ProductIds);

public sealed class SavePosFavoritesDto
{
    public long? Revision { get; init; }
    public int[]? ProductIds { get; init; }
}
