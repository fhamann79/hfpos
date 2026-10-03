using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public static class UserListQuery
{
    public static async Task<PagedResultDto<UserListDto>> GetPageAsync(
        PosDbContext context, int companyId, int page, int pageSize,
        string? search, bool? isActive, int? roleId)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = context.Users.AsNoTracking().Where(user => user.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(user => user.Username.ToLower().Contains(term)
                || user.Email.ToLower().Contains(term));
        }
        if (isActive.HasValue)
            query = query.Where(user => user.IsActive == isActive.Value);
        if (roleId.HasValue)
            query = query.Where(user => user.RoleId == roleId.Value && user.Role.CompanyId == companyId);

        var totalItems = await query.CountAsync();
        var offset = ((long)page - 1) * pageSize;
        IReadOnlyList<UserListDto> items = Array.Empty<UserListDto>();
        if (offset <= int.MaxValue)
        {
            items = await query.OrderBy(user => user.Username).ThenBy(user => user.Id)
                .Skip((int)offset).Take(pageSize)
                .Select(user => new UserListDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    IsActive = user.IsActive,
                    RoleId = user.RoleId,
                    RoleCode = user.Role.CompanyId == companyId ? user.Role.Code : "",
                    RoleName = user.Role.CompanyId == companyId ? user.Role.Name : "",
                    CompanyId = user.CompanyId,
                    EstablishmentId = user.EstablishmentId,
                    EmissionPointId = user.EmissionPointId
                }).ToListAsync();
        }
        return new PagedResultDto<UserListDto>
        {
            Items = items, Page = page, PageSize = pageSize, TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize)
        };
    }
}
