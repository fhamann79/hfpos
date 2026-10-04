using System.ComponentModel.DataAnnotations;

namespace Pos.Backend.Api.Core.DTOs;

public sealed class InitialDataPreviewRequest
{
    public Guid RequestId { get; set; }
    [Required, MaxLength(30)] public string Kind { get; set; } = string.Empty;
    [Required, MaxLength(1048576)] public string Csv { get; set; } = string.Empty;
    public string DuplicatePolicy { get; set; } = "create-only";
}

public sealed class InitialDataConfirmRequest
{
    [Required] public InitialDataPreviewRequest Payload { get; set; } = new();
    [Required, MaxLength(8192)] public string PreviewToken { get; set; } = string.Empty;
}

public sealed class InitialDataRowDto
{
    public int RowNumber { get; set; }
    public Dictionary<string, string> Values { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public int? ResolvedId { get; set; }
}

public sealed class InitialDataPreviewDto
{
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DuplicatePolicy { get; set; } = "create-only";
    public string Atomicity { get; set; } = "atomic-batch";
    public int MaxRows { get; set; } = 500;
    public bool CanConfirm { get; set; }
    public string? PreviewToken { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<InitialDataRowDto> Rows { get; set; } = new();
}

public sealed class InitialDataResultDto
{
    public int BatchId { get; set; }
    public Guid RequestId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public int CompanyId { get; set; }
    public int EstablishmentId { get; set; }
    public int EmissionPointId { get; set; }
    public int UserId { get; set; }
    public int RowCount { get; set; }
    public List<int> CreatedIds { get; set; } = new();
    public List<int> RowNumbers { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public sealed record TenantReadinessCheckDto(string Code, bool Ready, string Route);
public sealed record TenantReadinessDto(int CompanyId, int EstablishmentId, int EmissionPointId,
    bool Ready, IReadOnlyList<TenantReadinessCheckDto> Checks);
