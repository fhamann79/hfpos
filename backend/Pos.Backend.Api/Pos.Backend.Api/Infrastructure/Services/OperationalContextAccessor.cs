using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public class OperationalContextAccessor : IOperationalContextAccessor
{
    private const string ContextItemKey = "__OperationalContext";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PosDbContext _dbContext;
    private readonly ILogger<OperationalContextAccessor> _logger;

    public OperationalContextAccessor(
        IHttpContextAccessor httpContextAccessor,
        PosDbContext dbContext,
        ILogger<OperationalContextAccessor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<OperationalContext> GetRequiredContextAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext
            ?? throw LogAndCreateException("INVALID_CLAIMS", StatusCodes.Status401Unauthorized);

        if (httpContext.Items.TryGetValue(ContextItemKey, out var cached)
            && cached is OperationalContext cachedContext)
        {
            return cachedContext;
        }

        var principal = httpContext.User;
        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        var username = principal.FindFirstValue(AppClaims.Username);
        var companyIdValue = principal.FindFirstValue(AppClaims.CompanyId);
        var establishmentIdValue = principal.FindFirstValue(AppClaims.EstablishmentId);
        var emissionPointIdValue = principal.FindFirstValue(AppClaims.EmissionPointId);

        if (string.IsNullOrWhiteSpace(userIdValue)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(companyIdValue)
            || string.IsNullOrWhiteSpace(establishmentIdValue)
            || string.IsNullOrWhiteSpace(emissionPointIdValue)
            || !int.TryParse(userIdValue, out var userId)
            || !int.TryParse(companyIdValue, out var companyId)
            || !int.TryParse(establishmentIdValue, out var establishmentId)
            || !int.TryParse(emissionPointIdValue, out var emissionPointId))
        {
            throw LogAndCreateException("INVALID_CLAIMS", StatusCodes.Status401Unauthorized);
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.IsActive,
                u.CompanyId,
                u.EstablishmentId,
                u.EmissionPointId
            })
            .FirstOrDefaultAsync();

        if (user is null || !user.IsActive)
        {
            throw LogAndCreateException("CONTEXT_MISMATCH", StatusCodes.Status403Forbidden, userId, companyId, establishmentId, emissionPointId, username);
        }

        if (!string.Equals(user.Username, username, StringComparison.Ordinal))
        {
            throw LogAndCreateException("CONTEXT_MISMATCH", StatusCodes.Status403Forbidden, userId, companyId, establishmentId, emissionPointId, username);
        }

        var company = await _dbContext.Companies
            .AsNoTracking()
            .Where(c => c.Id == companyId)
            .Select(c => new { c.Id, c.IsActive })
            .FirstOrDefaultAsync();

        if (company is null || !company.IsActive)
        {
            throw LogAndCreateException("COMPANY_INACTIVE_OR_NOT_FOUND", StatusCodes.Status401Unauthorized, userId, companyId, establishmentId, emissionPointId, username);
        }

        var establishment = await _dbContext.Establishments
            .AsNoTracking()
            .Where(e => e.Id == establishmentId)
            .Select(e => new { e.Id, e.IsActive, e.CompanyId })
            .FirstOrDefaultAsync();

        if (establishment is null || !establishment.IsActive)
        {
            throw LogAndCreateException("ESTABLISHMENT_INACTIVE_OR_NOT_FOUND", StatusCodes.Status401Unauthorized, userId, companyId, establishmentId, emissionPointId, username);
        }

        if (establishment.CompanyId != companyId)
        {
            throw LogAndCreateException("CONTEXT_MISMATCH", StatusCodes.Status403Forbidden, userId, companyId, establishmentId, emissionPointId, username);
        }

        var emissionPoint = await _dbContext.EmissionPoints
            .AsNoTracking()
            .Where(ep => ep.Id == emissionPointId)
            .Select(ep => new { ep.Id, ep.IsActive, ep.EstablishmentId })
            .FirstOrDefaultAsync();

        if (emissionPoint is null || !emissionPoint.IsActive)
        {
            throw LogAndCreateException("EMISSION_POINT_INACTIVE_OR_NOT_FOUND", StatusCodes.Status401Unauthorized, userId, companyId, establishmentId, emissionPointId, username);
        }

        if (emissionPoint.EstablishmentId != establishmentId
            || user.CompanyId != companyId
            || user.EstablishmentId != establishmentId
            || user.EmissionPointId != emissionPointId)
        {
            throw LogAndCreateException("CONTEXT_MISMATCH", StatusCodes.Status403Forbidden, userId, companyId, establishmentId, emissionPointId, username);
        }

        var operationalContext = new OperationalContext
        {
            UserId = userId,
            Username = username,
            CompanyId = companyId,
            EstablishmentId = establishmentId,
            EmissionPointId = emissionPointId
        };

        httpContext.Items[ContextItemKey] = operationalContext;
        return operationalContext;
    }

    private OperationalContextException LogAndCreateException(
        string errorCode,
        int statusCode,
        int? userId = null,
        int? companyId = null,
        int? establishmentId = null,
        int? emissionPointId = null,
        string? username = null)
    {
        _logger.LogWarning(
            "Operational context error {ErrorCode}. UserId {UserId} Username {Username} CompanyId {CompanyId} EstablishmentId {EstablishmentId} EmissionPointId {EmissionPointId}",
            errorCode,
            userId,
            username,
            companyId,
            establishmentId,
            emissionPointId);

        return new OperationalContextException(errorCode, statusCode);
    }
}
