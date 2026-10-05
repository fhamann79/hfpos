namespace Pos.Backend.Api.Core.DTOs;

public sealed record IssueRecoveryDto(string Reason, string DeliveryReference, bool IdentityConfirmed, string? CurrentPassword = null);
public sealed record CompleteRecoveryDto(string? Token, string? NewPassword);
public sealed record SelfPasswordChangeDto(string CurrentPassword, string NewPassword);
public sealed record RecoveryIssuedDto(Guid Id, string Token, DateTime ExpiresAt);
public sealed record RecoveryStatusDto(Guid Id, DateTime ExpiresAt, DateTime? ConsumedAt, DateTime? RevokedAt);
public sealed record RecoveryAccountDto(int Id, string Username, bool IsActive);
