using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly JwtService _jwt;

    public AuthController(AuthService auth, JwtService jwt)
    {
        _auth = auth;
        _jwt = jwt;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        await Task.CompletedTask;

        return BadRequest(Error(
            "PUBLIC_REGISTRATION_NOT_SUPPORTED",
            "Public registration is not supported. Users must be created via administrative module."));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var (user, error) = await _auth.ValidateLoginAsync(dto);

        if (user == null)
        {
            return Unauthorized(Error(error ?? "INVALID_CREDENTIALS"));
        }

        var token = _jwt.GenerateToken(user);

        return Ok(new { token });
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var username = User.FindFirst(AppClaims.Username)?.Value;
        var companyIdValue = User.FindFirst(AppClaims.CompanyId)?.Value;
        var establishmentIdValue = User.FindFirst(AppClaims.EstablishmentId)?.Value;
        var emissionPointIdValue = User.FindFirst(AppClaims.EmissionPointId)?.Value;
        var roleCode = User.FindFirstValue(ClaimTypes.Role);
        var permissions = User.FindAll(AppClaims.Permission)
            .Select(claim => claim.Value)
            .Distinct()
            .ToArray();

        if (string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(companyIdValue)
            || string.IsNullOrWhiteSpace(establishmentIdValue)
            || string.IsNullOrWhiteSpace(emissionPointIdValue)
            || string.IsNullOrWhiteSpace(roleCode))
        {
            return Unauthorized(Error("INVALID_CLAIMS"));
        }

        if (!int.TryParse(companyIdValue, out var companyId)
            || !int.TryParse(establishmentIdValue, out var establishmentId)
            || !int.TryParse(emissionPointIdValue, out var emissionPointId))
        {
            return Unauthorized(Error("INVALID_CLAIMS"));
        }

        return Ok(new
        {
            userId,
            username,
            companyId,
            establishmentId,
            emissionPointId,
            roleCode,
            permissions
        });
    }

    private static ApiErrorResponse Error(string error, string? details = null)
        => new()
        {
            Error = error,
            Details = details
        };
}
