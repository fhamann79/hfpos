using Pos.Backend.Api.Core.Models;

namespace Pos.Backend.Api.Core.DTOs;

public sealed record FiscalTenantTrustSnapshot
{
    public int CompanyId { get; init; }
    public string Ruc { get; init; } = "";
    public bool CompanyActive { get; init; }
    public int? CertificateId { get; init; }
    public string? Thumbprint { get; init; }
    public DateTime? NotAfter { get; init; }
    public DateTime? NotBefore { get; init; }
    public bool? HasPrivateKey { get; init; }
    public string? SerialNumber { get; init; }
    public DateTime? UploadedAt { get; init; }
    public string? RowDigest { get; init; }
    public int? Environment { get; init; }
    public bool? SriEnabled { get; init; }
    public DateTime? SettingsUpdatedAt { get; init; }
}

public sealed record FiscalTenantPreflightDto(FiscalTenantTrustSnapshot Snapshot, string State, string Reason,
    DateTime? AsOf, FiscalTrustDecision? CandidateRc6, string Rc5Baseline = "UNKNOWN_OPERATOR_BASELINE_REQUIRED");

public sealed record FiscalPreflightPageDto(string TrustVersion, string RegistryStatus, DateTime GeneratedAt,
    DateTime? LastCompletedCycleAt, int TotalCompanies, int Page, int PageSize, int TotalPages,
    IReadOnlyList<FiscalTenantPreflightDto> Items);
