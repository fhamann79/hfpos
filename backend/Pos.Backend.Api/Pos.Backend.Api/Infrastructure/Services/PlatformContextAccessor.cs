using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PlatformContextAccessor(IHttpContextAccessor http, PosDbContext database) : IPlatformContextAccessor
{
    public async Task<PlatformContext> GetRequiredContextAsync()
    {
        var request = http.HttpContext ?? throw new PlatformException("INVALID_PLATFORM_CLAIMS", 401);
        if (request.Items.TryGetValue(typeof(PlatformContext), out var cached) && cached is PlatformContext context)
            return context;
        var user = request.User;
        if (user.Identity?.IsAuthenticated != true
            || !user.HasClaim(PlatformClaims.TokenType, PlatformClaims.TokenTypeValue)
            || !user.IsInRole(PlatformClaims.AdminRole)
            || user.HasClaim(c => c.Type == AppClaims.CompanyId || c.Type == AppClaims.EstablishmentId
                || c.Type == AppClaims.EmissionPointId || c.Type == AppClaims.Permission)
            || !int.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            || id <= 0 || string.IsNullOrWhiteSpace(user.FindFirstValue(AppClaims.Username)))
            throw new PlatformException("INVALID_PLATFORM_CLAIMS", 401);
        if (!long.TryParse(user.FindFirstValue(PlatformClaims.SessionVersion), NumberStyles.None,
            CultureInfo.InvariantCulture, out var version) || version <= 0)
            throw new PlatformException("PLATFORM_SESSION_STALE", 401);
        var proposed = new PlatformContext(id, user.FindFirstValue(AppClaims.Username)!, "", version);
        context = await RevalidateAsync(database, proposed);
        request.Items[typeof(PlatformContext)] = context;
        return context;
    }

    internal static async Task<PlatformContext> RevalidateAsync(PosDbContext database, PlatformContext proposed)
    {
        var current = await database.PlatformUsers.AsNoTracking().Where(u => u.Id == proposed.UserId)
            .Select(u => new { u.Username, u.Email, u.IsActive, u.SessionVersion }).SingleOrDefaultAsync();
        if (current is null || !current.IsActive)
            throw new PlatformException("PLATFORM_USER_INACTIVE_OR_NOT_FOUND", 401);
        if (current.SessionVersion != proposed.SessionVersion)
            throw new PlatformException("PLATFORM_SESSION_STALE", 401);
        if (current.Username != proposed.Username)
            throw new PlatformException("INVALID_PLATFORM_CLAIMS", 401);
        return proposed with { Email = current.Email };
    }
}
