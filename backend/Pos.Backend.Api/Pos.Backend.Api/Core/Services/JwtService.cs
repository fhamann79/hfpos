using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Core.Services;

public class JwtService
{
    private readonly PosDbContext _context;
    private readonly JwtOptions _jwtOptions;

    public JwtService(IOptions<JwtOptions> jwtOptions, PosDbContext context)
    {
        _jwtOptions = jwtOptions.Value;
        _context = context;
    }

    public string GenerateToken(User user)
    {
        var role = _context.Roles
            .AsNoTracking()
            .Where(r => r.Id == user.RoleId && r.CompanyId == user.CompanyId && r.IsActive)
            .Select(r => new { r.Code, r.AuthorizationVersion })
            .FirstOrDefault()
            ?? throw new OperationalContextException("ROLE_INACTIVE_OR_INVALID", StatusCodes.Status401Unauthorized);

        var permissions = _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == user.RoleId && rp.Role.CompanyId == user.CompanyId && rp.Permission.IsActive)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(AppClaims.Username, user.Username),
            new Claim(AppClaims.CompanyId, user.CompanyId.ToString()),
            new Claim(AppClaims.EstablishmentId, user.EstablishmentId!.Value.ToString()),
            new Claim(AppClaims.EmissionPointId, user.EmissionPointId.ToString()),
            new Claim(ClaimTypes.Role, role.Code),
            new Claim(AppClaims.UserSessionVersion, user.SessionVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim(AppClaims.RoleAuthorizationVersion, role.AuthorizationVersion.ToString(CultureInfo.InvariantCulture))
        };

        foreach (var permission in permissions)
        {
            claims.Add(new Claim(AppClaims.Permission, permission));
        }

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtOptions.Key)
        );

        var creds = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiresMinutes),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
