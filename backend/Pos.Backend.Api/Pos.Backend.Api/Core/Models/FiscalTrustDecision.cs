namespace Pos.Backend.Api.Core.Models;

public enum FiscalEvidenceState { Good, Revoked, Suspended, Unknown, Stale, Error }
public enum FiscalIdentityState { Match, Mismatch, NotConfirmed }

public sealed record FiscalTrustDecision(
    string TrustVersion, DateTime EvaluatedAt, DateTime ValidUntil, string CertificateSha256,
    bool ChainTrusted, FiscalIdentityState Identity, bool TimeValid, bool SigningSuitable,
    FiscalEvidenceState Revocation, string Reason, string IdentityEvidence)
{
    public bool Eligible => ChainTrusted && Identity != FiscalIdentityState.Mismatch && TimeValid && SigningSuitable
        && Revocation == FiscalEvidenceState.Good;
}
