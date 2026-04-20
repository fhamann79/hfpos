using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pos.Backend.Api.Configuration;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;
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
        var roleCode = user.Role?.Code;

        if (string.IsNullOrWhiteSpace(roleCode) && user.RoleId > 0)
        {
            roleCode = _context.Roles
                .AsNoTracking()
                .Where(r => r.Id == user.RoleId)
                .Select(r => r.Code)
                .FirstOrDefault();
        }

        var permissions = _context.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == user.RoleId && rp.Permission.IsActive)
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
            new Claim(ClaimTypes.Role, roleCode ?? string.Empty)
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
