using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Infrastructure.Services;

namespace Pos.Backend.Api.Infrastructure.Data;

public static class PlatformBootstrap
{
    public static async Task RunAsync(PosDbContext context, PlatformBootstrapOptions options)
    {
        if (!options.Enabled) return;
        await using var transaction = await context.Database.BeginTransactionAsync();
        // Serializes concurrent first-start bootstraps without resetting any existing identity.
        await context.Database.ExecuteSqlRawAsync("LOCK TABLE \"PlatformUsers\" IN SHARE ROW EXCLUSIVE MODE");
        if (await context.PlatformUsers.AnyAsync()) return;
        var username = options.Username?.Trim() ?? "";
        var email = options.Email?.Trim() ?? "";
        if (!PlatformAuthService.ValidIdentity(username, email, options.Password))
            throw new InvalidOperationException("PLATFORM_BOOTSTRAP_CONFIGURATION_INVALID");
        var user = new PlatformUser { Username = username, Email = email, CreatedAt = DateTime.UtcNow };
        user.PasswordHash = new PasswordHasher<PlatformUser>().HashPassword(user, options.Password);
        context.PlatformUsers.Add(user);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
