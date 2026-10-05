using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PasswordRecoveryService(PosDbContext db,
    IOperationalContextAccessor tenantAccessor, IPlatformContextAccessor platformAccessor)
{
    public const int LifetimeMinutes = 15;
    public const string InvalidChallenge = "RECOVERY_INVALID";
    private readonly PasswordHasher<User> tenantHasher = new();
    private readonly PasswordHasher<PlatformUser> platformHasher = new();

    public async Task<RecoveryIssuedDto> IssueAsync(bool platform, int targetId, IssueRecoveryDto request)
    {
        if (!request.IdentityConfirmed || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500
            || string.IsNullOrWhiteSpace(request.DeliveryReference) || request.DeliveryReference.Length > 500)
            throw Error("RECOVERY_IDENTITY_REQUIRED");
        var actor = await ActorAsync(platform);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(platform, actor.CompanyId);
        await ValidateActorAsync(platform, actor, request.CurrentPassword);
        var now = DateTime.UtcNow;
        var challenge = new PasswordRecoveryChallenge
        {
            Id = Guid.NewGuid(), IsPlatform = platform, CompanyId = actor.CompanyId, CreatedById = actor.Id,
            CreatedAt = now, ExpiresAt = now.AddMinutes(LifetimeMinutes),
            Reason = request.Reason.Trim(), DeliveryReference = request.DeliveryReference.Trim()
        };
        if (platform)
        {
            var user = await db.PlatformUsers.SingleOrDefaultAsync(u => u.Id == targetId);
            if (user is null || !user.IsActive || user.Id == actor.Id) throw Error("RECOVERY_NOT_ALLOWED", 409);
            challenge.PlatformUserId = user.Id; challenge.SessionVersion = user.SessionVersion;
            challenge.PasswordFingerprint = Fingerprint(user.PasswordHash);
        }
        else
        {
            var user = await TenantUserAsync(targetId, actor.CompanyId!.Value);
            if (!Active(user)) throw Error("RECOVERY_NOT_ALLOWED", 409);
            BindTenant(challenge, user!);
        }
        // Reissuing retires prior links; only this response ever contains the new secret.
        await db.PasswordRecoveryChallenges.Where(c => c.IsPlatform == platform && c.CompanyId == actor.CompanyId
            && (platform ? c.PlatformUserId == targetId : c.UserId == targetId)
            && c.ConsumedAt == null && c.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now));
        var token = $"{(platform ? 'p' : 't')}.{challenge.Id:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        challenge.TokenHash = Hash(token, challenge);
        db.PasswordRecoveryChallenges.Add(challenge);
        Audit(platform, actor.CompanyId, actor.Id, targetId, "Issued", challenge);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return new(challenge.Id, token, challenge.ExpiresAt);
    }

    public async Task CompleteAsync(bool platform, CompleteRecoveryDto request)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword)) throw Error("PASSWORD_POLICY_INVALID");
        var parsed = Parse(request.Token, platform);
        if (parsed is null) { await FailureAsync(platform, null); throw Error(InvalidChallenge); }
        var scope = await db.PasswordRecoveryChallenges.AsNoTracking().Where(c => c.Id == parsed && c.IsPlatform == platform)
            .Select(c => new { c.CompanyId }).SingleOrDefaultAsync();
        if (scope is null) { await FailureAsync(platform, null); throw Error(InvalidChallenge); }
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(platform, scope.CompanyId);
        var challenge = await db.PasswordRecoveryChallenges.SingleAsync(c => c.Id == parsed);
        var now = DateTime.UtcNow;
        var valid = challenge.IsPlatform == platform && challenge.CompanyId == scope.CompanyId
            && challenge.ExpiresAt > now && challenge.RevokedAt is null && challenge.ConsumedAt is null
            && CryptographicOperations.FixedTimeEquals(challenge.TokenHash, Hash(request.Token!, challenge));
        User? tenant = null; PlatformUser? account = null;
        if (valid && platform)
        {
            account = await db.PlatformUsers.SingleOrDefaultAsync(u => u.Id == challenge.PlatformUserId);
            valid = account is { IsActive: true } && account.SessionVersion == challenge.SessionVersion
                && CryptographicOperations.FixedTimeEquals(challenge.PasswordFingerprint, Fingerprint(account.PasswordHash));
        }
        else if (valid)
        {
            tenant = await TenantUserAsync(challenge.UserId!.Value, challenge.CompanyId!.Value);
            valid = Active(tenant) && tenant!.SessionVersion == challenge.SessionVersion && tenant.RoleId == challenge.RoleId
                && tenant.Role.AuthorizationVersion == challenge.RoleVersion && tenant.EstablishmentId == challenge.EstablishmentId
                && tenant.EmissionPointId == challenge.EmissionPointId
                && CryptographicOperations.FixedTimeEquals(challenge.PasswordFingerprint, Fingerprint(tenant.PasswordHash));
        }
        challenge.AttemptCount = checked(challenge.AttemptCount + 1); challenge.LastAttemptAt = now;
        if (!valid)
        {
            Audit(platform, challenge.CompanyId, null, challenge.UserId ?? challenge.PlatformUserId, "Rejected", challenge);
            await db.SaveChangesAsync(); await tx.CommitAsync(); throw Error(InvalidChallenge);
        }
        if (platform)
        {
            account!.PasswordHash = platformHasher.HashPassword(account, request.NewPassword!);
            account.SessionVersion = checked(account.SessionVersion + 1);
        }
        else
        {
            tenant!.PasswordHash = tenantHasher.HashPassword(tenant, request.NewPassword!);
            tenant.SessionVersion = checked(tenant.SessionVersion + 1);
        }
        challenge.ConsumedAt = now;
        Audit(platform, challenge.CompanyId, null, challenge.UserId ?? challenge.PlatformUserId, "Consumed", challenge);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task RevokeAsync(bool platform, Guid id, string? currentPassword)
    {
        var actor = await ActorAsync(platform);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(platform, actor.CompanyId);
        await ValidateActorAsync(platform, actor, currentPassword);
        var challenge = await db.PasswordRecoveryChallenges.SingleOrDefaultAsync(c => c.Id == id
            && c.IsPlatform == platform && c.CompanyId == actor.CompanyId);
        if (challenge is null) throw Error("RECOVERY_NOT_FOUND", 404);
        if (challenge.ConsumedAt is null && challenge.RevokedAt is null)
        {
            challenge.RevokedAt = DateTime.UtcNow;
            Audit(platform, actor.CompanyId, actor.Id, challenge.UserId ?? challenge.PlatformUserId, "Revoked", challenge);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }

    public async Task<IReadOnlyList<RecoveryStatusDto>> StatusAsync(bool platform, int targetId)
    {
        var actor = await ActorAsync(platform);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(platform, actor.CompanyId);
        await ValidateActorAsync(platform, actor, null, reauthenticate: false);
        return await db.PasswordRecoveryChallenges.AsNoTracking().Where(c => c.IsPlatform == platform && c.CompanyId == actor.CompanyId
            && (platform ? c.PlatformUserId == targetId : c.UserId == targetId)).OrderByDescending(c => c.CreatedAt).Take(10)
            .Select(c => new RecoveryStatusDto(c.Id, c.ExpiresAt, c.ConsumedAt, c.RevokedAt)).ToListAsync();
    }

    public async Task<IReadOnlyList<RecoveryAccountDto>> PlatformAccountsAsync()
    {
        var actor = await ActorAsync(true);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(true, null); await ValidateActorAsync(true, actor, null, false);
        return await db.PlatformUsers.AsNoTracking().OrderBy(u => u.Id).Take(100)
            .Select(u => new RecoveryAccountDto(u.Id, u.Username, u.IsActive)).ToListAsync();
    }

    public async Task ChangeSelfAsync(bool platform, SelfPasswordChangeDto request)
    {
        if (!PasswordPolicy.IsValid(request.NewPassword)) throw Error("PASSWORD_POLICY_INVALID");
        var actor = await ActorAsync(platform);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockScopeAsync(platform, actor.CompanyId);
        await ValidateActorAsync(platform, actor, request.CurrentPassword, manage: false);
        if (platform)
        {
            var user = await db.PlatformUsers.SingleAsync(u => u.Id == actor.Id);
            user.PasswordHash = platformHasher.HashPassword(user, request.NewPassword); user.SessionVersion = checked(user.SessionVersion + 1);
        }
        else
        {
            var user = await TenantUserAsync(actor.Id, actor.CompanyId!.Value);
            if (!Verify(tenantHasher, user!, request.CurrentPassword)) throw Error("CURRENT_PASSWORD_INVALID", 400);
            user!.PasswordHash = tenantHasher.HashPassword(user, request.NewPassword); user.SessionVersion = checked(user.SessionVersion + 1);
        }
        var now = DateTime.UtcNow;
        await db.PasswordRecoveryChallenges.Where(c => c.IsPlatform == platform && c.CompanyId == actor.CompanyId
            && (platform ? c.PlatformUserId == actor.Id : c.UserId == actor.Id) && c.ConsumedAt == null && c.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevokedAt, now));
        Audit(platform, actor.CompanyId, actor.Id, actor.Id, "PasswordChanged", null);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    private async Task<(int Id, int? CompanyId, long Version, long? RoleVersion)> ActorAsync(bool platform)
    {
        if (platform) { var a = await platformAccessor.GetRequiredContextAsync(); return (a.UserId, null, a.SessionVersion, null); }
        var tenant = await tenantAccessor.GetRequiredContextAsync();
        return (tenant.UserId, tenant.CompanyId, tenant.UserSessionVersion!.Value, tenant.RoleAuthorizationVersion);
    }

    private async Task ValidateActorAsync(bool platform, (int Id, int? CompanyId, long Version, long? RoleVersion) actor,
        string? currentPassword, bool reauthenticate = true, bool manage = true)
    {
        if (platform)
        {
            var user = await db.PlatformUsers.SingleOrDefaultAsync(u => u.Id == actor.Id);
            if (user is null || !user.IsActive || user.SessionVersion != actor.Version) throw Error("PLATFORM_SESSION_STALE", 401);
            if (reauthenticate && !Verify(platformHasher, user, currentPassword)) throw Error("CURRENT_PASSWORD_INVALID", 400);
        }
        else
        {
            var user = await TenantUserAsync(actor.Id, actor.CompanyId!.Value);
            if (!Active(user) || user!.SessionVersion != actor.Version || user.Role.AuthorizationVersion != actor.RoleVersion)
                throw Error("SESSION_STALE", 401);
            if (manage && (user.Role.Code == AppRoles.Cashier || !await db.RolePermissions.AnyAsync(rp => rp.RoleId == user.RoleId
                && rp.Permission.IsActive && rp.Permission.Code == AppPermissions.UsersRecoveryManage))) throw Error("FORBIDDEN", 403);
        }
    }

    private async Task LockScopeAsync(bool platform, int? companyId)
    {
        // Match existing tenant lifecycle lock order; platform has no fabricated operational context.
        if (platform) await db.Database.ExecuteSqlRawAsync("LOCK TABLE \"PlatformUsers\" IN SHARE ROW EXCLUSIVE MODE");
        else
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Companies\" WHERE \"Id\" = {companyId} FOR UPDATE");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Roles\" WHERE \"CompanyId\" = {companyId} FOR SHARE");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Establishments\" WHERE \"CompanyId\" = {companyId} FOR SHARE");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"EmissionPoints\" WHERE \"EstablishmentId\" IN (SELECT \"Id\" FROM \"Establishments\" WHERE \"CompanyId\" = {companyId}) FOR SHARE");
        }
    }

    private Task<User?> TenantUserAsync(int id, int companyId) => db.Users.Include(u => u.Company).Include(u => u.Role)
        .Include(u => u.Establishment).Include(u => u.EmissionPoint).SingleOrDefaultAsync(u => u.Id == id && u.CompanyId == companyId);
    private static bool Active(User? u) => u is { IsActive: true, Company.IsActive: true, Role.IsActive: true,
        Establishment.IsActive: true, EmissionPoint.IsActive: true } && u.Role.CompanyId == u.CompanyId
        && u.Establishment.CompanyId == u.CompanyId && u.EmissionPoint.EstablishmentId == u.EstablishmentId;
    private static bool Verify<T>(PasswordHasher<T> hasher, T user, string? password) where T : class
        => password is { Length: > 0 and <= 256 } && hasher.VerifyHashedPassword(user,
            user is User u ? u.PasswordHash : ((PlatformUser)(object)user).PasswordHash, password) != PasswordVerificationResult.Failed;
    private static void BindTenant(PasswordRecoveryChallenge c, User u)
    {
        c.UserId = u.Id; c.CompanyId = u.CompanyId; c.SessionVersion = u.SessionVersion;
        c.RoleId = u.RoleId; c.RoleVersion = u.Role.AuthorizationVersion; c.EstablishmentId = u.EstablishmentId;
        c.EmissionPointId = u.EmissionPointId; c.PasswordFingerprint = Fingerprint(u.PasswordHash);
    }
    private static byte[] Fingerprint(string hash) => SHA256.HashData(Encoding.UTF8.GetBytes(hash));
    private static byte[] Hash(string token, PasswordRecoveryChallenge c) => SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"{token}|{c.Id:N}|{c.IsPlatform}|{c.CompanyId}|{c.UserId}|{c.PlatformUserId}|{c.SessionVersion}|{c.RoleId}|{c.RoleVersion}|{c.EstablishmentId}|{c.EmissionPointId}|{Convert.ToHexString(c.PasswordFingerprint)}")));
    private static Guid? Parse(string? token, bool platform)
    {
        if (token is null || token.Length != 99 || token[0] != (platform ? 'p' : 't') || token[1] != '.' || token[34] != '.') return null;
        if (!Guid.TryParseExact(token.AsSpan(2, 32), "N", out var id) || token.AsSpan(35).ContainsAnyExcept("0123456789ABCDEF")) return null;
        return id;
    }
    private void Audit(bool platform, int? company, int? actor, int? target, string outcome, PasswordRecoveryChallenge? c)
        => db.PasswordSecurityAudits.Add(new() { IsPlatform = platform, CompanyId = company, ActorId = actor, TargetId = target,
            ChallengeId = c?.Id, Event = outcome, Reason = c?.Reason ?? "", DeliveryReference = c?.DeliveryReference ?? "", CreatedAt = DateTime.UtcNow });
    private async Task FailureAsync(bool platform, PasswordRecoveryChallenge? c)
    { Audit(platform, c?.CompanyId, null, c?.UserId ?? c?.PlatformUserId, "Rejected", c); await db.SaveChangesAsync(); }
    private static OperationalContextException Error(string code, int status = 400) => new(code, status);
}
