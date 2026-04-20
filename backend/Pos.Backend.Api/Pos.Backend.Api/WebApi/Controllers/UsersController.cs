using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly PosDbContext _context;
    private readonly PasswordHasher<User> _hasher = new();

    public UsersController(PosDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Authorize(Policy = AppPermissions.AdminUsersRead)]
    public async Task<ActionResult<IEnumerable<UserListDto>>> Get()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderBy(u => u.Username)
            .Select(u => new UserListDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsActive = u.IsActive,
                RoleId = u.RoleId,
                RoleCode = u.Role.Code,
                RoleName = u.Role.Name,
                CompanyId = u.CompanyId,
                EstablishmentId = u.EstablishmentId,
                EmissionPointId = u.EmissionPointId
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminUsersRead)]
    public async Task<ActionResult<UserDetailDto>> GetById(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Id == id)
            .Select(u => new UserDetailDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsActive = u.IsActive,
                RoleId = u.RoleId,
                RoleCode = u.Role.Code,
                RoleName = u.Role.Name,
                CompanyId = u.CompanyId,
                EstablishmentId = u.EstablishmentId,
                EmissionPointId = u.EmissionPointId
            })
            .FirstOrDefaultAsync();

        if (user is null)
        {
            return NotFound(new ApiErrorResponse { Error = "USER_NOT_FOUND" });
        }

        return Ok(user);
    }

    [HttpPost]
    [Authorize(Policy = AppPermissions.AdminUsersWrite)]
    public async Task<ActionResult<UserDetailDto>> Create([FromBody] UserCreateDto dto)
    {
        var validationError = await ValidateUserDataAsync(dto.Username, dto.Email, dto.RoleId, dto.CompanyId, dto.EstablishmentId, dto.EmissionPointId);
        if (validationError is not null)
        {
            return validationError;
        }

        if (string.IsNullOrWhiteSpace(dto.Password))
        {
            return BadRequest(new ApiErrorResponse { Error = "PASSWORD_REQUIRED" });
        }

        var user = new User
        {
            Username = dto.Username.Trim(),
            Email = dto.Email.Trim(),
            RoleId = dto.RoleId,
            CompanyId = dto.CompanyId,
            EstablishmentId = dto.EstablishmentId,
            EmissionPointId = dto.EmissionPointId,
            IsActive = dto.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        user.PasswordHash = _hasher.HashPassword(user, dto.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var created = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.Id == user.Id)
            .Select(u => new UserDetailDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsActive = u.IsActive,
                RoleId = u.RoleId,
                RoleCode = u.Role.Code,
                RoleName = u.Role.Name,
                CompanyId = u.CompanyId,
                EstablishmentId = u.EstablishmentId,
                EmissionPointId = u.EmissionPointId
            })
            .FirstAsync();

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, created);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminUsersWrite)]
    public async Task<IActionResult> Update(int id, [FromBody] UserUpdateDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new ApiErrorResponse { Error = "USER_NOT_FOUND" });
        }

        var validationError = await ValidateUserDataAsync(user.Username, dto.Email, dto.RoleId, dto.CompanyId, dto.EstablishmentId, dto.EmissionPointId, id);
        if (validationError is not null)
        {
            return validationError;
        }

        user.Email = dto.Email.Trim();
        user.RoleId = dto.RoleId;
        user.CompanyId = dto.CompanyId;
        user.EstablishmentId = dto.EstablishmentId;
        user.EmissionPointId = dto.EmissionPointId;
        user.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPut("{id:int}/password")]
    [Authorize(Policy = AppPermissions.AdminUsersWrite)]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangeUserPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto?.NewPassword))
        {
            return BadRequest(new ApiErrorResponse { Error = "NEW_PASSWORD_REQUIRED" });
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new ApiErrorResponse { Error = "USER_NOT_FOUND" });
        }

        user.PasswordHash = _hasher.HashPassword(user, dto.NewPassword);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = AppPermissions.AdminUsersWrite)]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return NotFound(new ApiErrorResponse { Error = "USER_NOT_FOUND" });
        }

        // Decisión explícita: soft-delete lógico.
        user.IsActive = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<ActionResult?> ValidateUserDataAsync(
        string? username,
        string? email,
        int roleId,
        int companyId,
        int? establishmentId,
        int emissionPointId,
        int? updatingUserId = null)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return BadRequest(new ApiErrorResponse { Error = "USERNAME_REQUIRED" });
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new ApiErrorResponse { Error = "EMAIL_REQUIRED" });
        }

        if (!establishmentId.HasValue)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_REQUIRED" });
        }

        var normalizedUsername = username.Trim();
        var normalizedEmail = email.Trim();

        var usernameExists = await _context.Users.AnyAsync(u =>
            u.Username == normalizedUsername && (!updatingUserId.HasValue || u.Id != updatingUserId.Value));

        if (usernameExists)
        {
            return Conflict(new ApiErrorResponse { Error = "USERNAME_ALREADY_EXISTS" });
        }

        var emailExists = await _context.Users.AnyAsync(u =>
            u.Email == normalizedEmail && (!updatingUserId.HasValue || u.Id != updatingUserId.Value));

        if (emailExists)
        {
            return Conflict(new ApiErrorResponse { Error = "EMAIL_ALREADY_EXISTS" });
        }

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.Id == roleId);
        if (role is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "ROLE_NOT_FOUND" });
        }

        if (!role.IsActive)
        {
            return BadRequest(new ApiErrorResponse { Error = "ROLE_INACTIVE" });
        }

        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Id == companyId);
        if (company is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "COMPANY_NOT_FOUND" });
        }

        var establishment = await _context.Establishments
            .FirstOrDefaultAsync(e => e.Id == establishmentId.Value);

        if (establishment is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_FOUND" });
        }

        if (establishment.CompanyId != companyId)
        {
            return BadRequest(new ApiErrorResponse { Error = "ESTABLISHMENT_NOT_IN_COMPANY" });
        }

        var emissionPoint = await _context.EmissionPoints
            .FirstOrDefaultAsync(ep => ep.Id == emissionPointId);

        if (emissionPoint is null)
        {
            return BadRequest(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_FOUND" });
        }

        if (emissionPoint.EstablishmentId != establishment.Id)
        {
            return BadRequest(new ApiErrorResponse { Error = "EMISSION_POINT_NOT_IN_ESTABLISHMENT" });
        }

        return null;
    }
}
