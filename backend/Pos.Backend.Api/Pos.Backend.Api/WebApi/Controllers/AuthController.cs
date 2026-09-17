using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.WebApi.Filters;
using System.Security.Claims;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    private readonly JwtService _jwt;
    private readonly IOperationalContextAccessor _operationalContext;

    public AuthController(AuthService auth, JwtService jwt, IOperationalContextAccessor operationalContext)
    {
        _auth = auth;
        _jwt = jwt;
        _operationalContext = operationalContext;
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
    [RequireOperationalContext]
    public async Task<IActionResult> Me()
    {
        var context = await _operationalContext.GetRequiredContextAsync();
        return Ok(new
        {
            userId = context.UserId.ToString(),
            username = context.Username,
            companyId = context.CompanyId,
            companyTimeZoneId = context.CompanyTimeZoneId,
            establishmentId = context.EstablishmentId,
            emissionPointId = context.EmissionPointId,
            roleCode = User.FindFirstValue(ClaimTypes.Role),
            permissions = User.FindAll(AppClaims.Permission).Select(c => c.Value).Distinct().ToArray()
        });
    }

    private static ApiErrorResponse Error(string error, string? details = null)
        => new()
        {
            Error = error,
            Details = details
        };
}
