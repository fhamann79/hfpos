using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PlatformAuthService(PosDbContext database, IOptions<JwtOptions> options) : IPlatformAuthService
{
    private readonly PasswordHasher<PlatformUser> _hasher = new();
    private static readonly PlatformUser Dummy = new();
    private static readonly string DummyHash = new PasswordHasher<PlatformUser>().HashPassword(Dummy, Guid.NewGuid().ToString());

    public async Task<string> LoginAsync(LoginDto request)
    {
        var user = await database.PlatformUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Username == request.Username.Trim());
        var verification = _hasher.VerifyHashedPassword(user ?? Dummy, user?.PasswordHash ?? DummyHash, request.Password ?? "");
        if (user is null || !user.IsActive || verification == PasswordVerificationResult.Failed)
            throw new PlatformException("INVALID_CREDENTIALS", 401);
        var jwt = options.Value;
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString(CultureInfo.InvariantCulture)),
            new Claim(AppClaims.Username, user.Username),
            new Claim(ClaimTypes.Role, PlatformClaims.AdminRole),
            new Claim(PlatformClaims.TokenType, PlatformClaims.TokenTypeValue),
            new Claim(PlatformClaims.SessionVersion, user.SessionVersion.ToString(CultureInfo.InvariantCulture))
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(jwt.Issuer, jwt.Audience,
            claims, expires: DateTime.UtcNow.AddMinutes(jwt.ExpiresMinutes),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                SecurityAlgorithms.HmacSha256)));
    }

    internal static bool ValidIdentity(string username, string email, string password)
        => !string.IsNullOrWhiteSpace(username) && username.Length <= 100
            && email.Length <= 320 && MailAddress.TryCreate(email, out var address) && address.Address == email
            && PasswordPolicy.IsValid(password);
}
