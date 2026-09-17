using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Core.Services;

public class AuthService
{
    private readonly PosDbContext _context;
    private readonly ILogger<AuthService> _logger;
    private readonly PasswordHasher<User> _hasher = new();

    public AuthService(PosDbContext context, ILogger<AuthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public Task<(bool Ok, string Error)> RegisterAsync(RegisterDto dto)
        => Task.FromResult((false, "PUBLIC_REGISTRATION_NOT_SUPPORTED"));

    public async Task<(User? User, string Error)> ValidateLoginAsync(LoginDto dto)
    {
        // 1) Traer user + Company + Establishment (para validar reglas)
        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.Establishment)
            .Include(u => u.EmissionPoint)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Username == dto.Username);

        // 2) Validación: usuario existe y está activo
        if (user is null || !user.IsActive)
        {
            _logger.LogWarning(
                "Login failed for {Username}. ErrorCode {ErrorCode}",
                dto.Username,
                "USER_INACTIVE_OR_NOT_FOUND");
            return (null, "USER_INACTIVE_OR_NOT_FOUND");
        }

        // 3) Validación: empresa existe y está activa
        if (user.Company is null || !user.Company.IsActive)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "COMPANY_INACTIVE_OR_NOT_FOUND");
            return (null, "COMPANY_INACTIVE_OR_NOT_FOUND");
        }

        // 4) Validación: usuario debe tener Establishment asignado
        if (user.EstablishmentId is null)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "ESTABLISHMENT_NOT_ASSIGNED");
            return (null, "ESTABLISHMENT_NOT_ASSIGNED");
        }

        // 5) Validación: establecimiento existe y está activo
        if (user.Establishment is null || !user.Establishment.IsActive)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "ESTABLISHMENT_INACTIVE_OR_NOT_FOUND");
            return (null, "ESTABLISHMENT_INACTIVE_OR_NOT_FOUND");
        }

        // 6) Validación: usuario debe tener EmissionPoint asignado
        if (user.EmissionPointId <= 0)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "EMISSION_POINT_NOT_ASSIGNED");
            return (null, "EMISSION_POINT_NOT_ASSIGNED");
        }

        // 7) Validación: punto de emisión existe y está activo
        if (user.EmissionPoint is null || !user.EmissionPoint.IsActive)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "EMISSION_POINT_INACTIVE_OR_NOT_FOUND");
            return (null, "EMISSION_POINT_INACTIVE_OR_NOT_FOUND");
        }

        if (user.Role is null || !user.Role.IsActive || user.Role.CompanyId != user.CompanyId)
        {
            return (null, "ROLE_INACTIVE_OR_INVALID");
        }

        if (user.Establishment.CompanyId != user.CompanyId
            || user.EmissionPoint.EstablishmentId != user.EstablishmentId)
        {
            return (null, "CONTEXT_MISMATCH");
        }

        // 8) Validación password hash
        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);

        if (result != PasswordVerificationResult.Success)
        {
            _logger.LogWarning(
                "Login failed for {Username}. UserId {UserId}. ErrorCode {ErrorCode}",
                dto.Username,
                user.Id,
                "INVALID_CREDENTIALS");

            return (null, "INVALID_CREDENTIALS");
        }

        _logger.LogInformation(
            "Login succeeded for {Username}. UserId {UserId} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
            user.Username,
            user.Id,
            user.CompanyId,
            user.EstablishmentId,
            user.EmissionPointId);

        return (user, "");
    }
}
