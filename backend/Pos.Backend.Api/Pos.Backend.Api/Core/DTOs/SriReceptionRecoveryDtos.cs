namespace Pos.Backend.Api.Core.DTOs;

public sealed record SriReceptionRecoveryRequest(string? Reference, string? Reason, bool ConfirmsResendDespiteUncertainReception);

public sealed record SriReceptionRecoveryStatus(string Code);
