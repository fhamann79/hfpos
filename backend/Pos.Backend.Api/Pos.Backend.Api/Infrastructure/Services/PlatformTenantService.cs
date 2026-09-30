using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pos.Backend.Api.Core.DTOs;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Models;
using Pos.Backend.Api.Core.Security;
using Pos.Backend.Api.Core.Services;
using Pos.Backend.Api.Infrastructure.Data;

namespace Pos.Backend.Api.Infrastructure.Services;

public sealed class PlatformTenantService(PosDbContext database, IPlatformContextAccessor accessor,
    IBusinessClockService clock, TenantAdministrationGuard guard) : IPlatformTenantService
{
    private static readonly Expression<Func<Company, PlatformTenantDto>> CompanyProjection = c => new()
    { Id = c.Id, Name = c.Name, Ruc = c.Ruc, TimeZoneId = c.TimeZoneId, IsActive = c.IsActive, CreatedAt = c.CreatedAt };
    private static readonly Expression<Func<PlatformTenantEvent, PlatformTenantEventDto>> EventProjection = e =>
        new(e.Id, e.EventType.ToString(), e.PlatformUser.Username, e.Reason, e.CreatedAt);

    public async Task<PagedResultDto<PlatformTenantDto>> GetAsync(PlatformTenantQuery request)
    {
        await accessor.GetRequiredContextAsync();
        var query = database.Companies.AsNoTracking();
        var search = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(c => c.Name.Contains(search) || c.Ruc.Contains(search));
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (request.Status is not ("Active" or "Suspended")) throw new PlatformException("TENANT_STATUS_INVALID");
            var active = request.Status == "Active";
            query = query.Where(c => c.IsActive == active);
        }
        var page = Math.Clamp(request.Page, 1, 1_000_000);
        var size = Math.Clamp(request.PageSize, 1, 200);
        var count = await query.CountAsync();
        return new PagedResultDto<PlatformTenantDto>
        {
            Items = await query.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                .Skip((page - 1) * size).Take(size).Select(CompanyProjection).ToListAsync(),
            Page = page, PageSize = size, TotalItems = count, TotalPages = (int)Math.Ceiling(count / (double)size)
        };
    }

    public async Task<PlatformTenantDetailDto> GetByIdAsync(int id)
    {
        await accessor.GetRequiredContextAsync();
        var company = await database.Companies.AsNoTracking().Where(c => c.Id == id)
            .Select(CompanyProjection).SingleOrDefaultAsync() ?? throw new PlatformException("TENANT_NOT_FOUND", 404);
        var counts = await database.Companies.Where(c => c.Id == id).Select(c => new
        {
            Establishments = c.Establishments.Count,
            ActiveEstablishments = c.Establishments.Count(e => e.IsActive),
            Points = database.EmissionPoints.Count(p => p.Establishment.CompanyId == id),
            ActivePoints = database.EmissionPoints.Count(p => p.Establishment.CompanyId == id && p.IsActive),
            Users = database.Users.Count(u => u.CompanyId == id),
            ActiveUsers = database.Users.Count(u => u.CompanyId == id && u.IsActive)
        }).SingleAsync();
        var events = (await GetEventsAsync(id, 1, 20)).Items;
        return new(company, counts.Establishments, counts.ActiveEstablishments, counts.Points,
            counts.ActivePoints, counts.Users, counts.ActiveUsers, events);
    }

    public async Task<PagedResultDto<PlatformTenantEventDto>> GetEventsAsync(int id, int page, int pageSize)
    {
        await accessor.GetRequiredContextAsync();
        if (!await database.Companies.AnyAsync(c => c.Id == id)) throw new PlatformException("TENANT_NOT_FOUND", 404);
        page = Math.Clamp(page, 1, 1_000_000);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var events = database.PlatformTenantEvents.AsNoTracking().Where(e => e.CompanyId == id);
        var count = await events.CountAsync();
        return new()
        {
            Items = await events.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).Select(EventProjection).ToListAsync(),
            Page = page, PageSize = pageSize, TotalItems = count, TotalPages = (int)Math.Ceiling(count / (double)pageSize)
        };
    }

    public async Task<TenantProvisionResult> ProvisionAsync(TenantProvisionRequest request)
    {
        var actor = await accessor.GetRequiredContextAsync();
        request = Normalize(request);
        var snapshot = JsonSerializer.Serialize(request with
        { RequestId = Guid.Empty, InitialAdmin = request.InitialAdmin with { Password = "" } });
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync();
            await LockPlatformActorAsync(actor);
            var prior = await ReplayAsync(actor, request, snapshot);
            if (prior is not null) return prior;
            var now = clock.UtcNow;
            var company = new Company { Name = request.Company.Name, Ruc = request.Company.Ruc,
                TimeZoneId = request.Company.TimeZoneId, IsActive = true, CreatedAt = now };
            var establishment = new Establishment { Company = company, Code = "001", Name = request.InitialEstablishment.Name,
                Address = request.InitialEstablishment.Address!, IsActive = true, CreatedAt = now };
            var point = new EmissionPoint { Establishment = establishment, Code = "001", Name = request.InitialEmissionPoint.Name,
                IsActive = true, CreatedAt = now };
            database.Companies.Add(company);
            database.Establishments.Add(establishment);
            database.EmissionPoints.Add(point);
            await database.SaveChangesAsync();
            // Only insert missing global catalog entries; never mutate existing/global permissions.
            foreach (var permission in TenantDefaults.Permissions)
                await database.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""Permissions""
                    (""Code"", ""Description"", ""IsActive"", ""CreatedAt"")
                    VALUES ({permission.Code}, {permission.Description}, TRUE, {now}) ON CONFLICT (""Code"") DO NOTHING");
            var permissions = await database.Permissions.Where(p => TenantDefaults.Permissions.Select(d => d.Code).Contains(p.Code))
                .ToDictionaryAsync(p => p.Code);
            var roles = TenantDefaults.Roles.Select(definition => new Role
                { CompanyId = company.Id, Code = definition.Code, Name = definition.Name, IsActive = true, CreatedAt = now }).ToArray();
            database.Roles.AddRange(roles);
            foreach (var role in roles)
                foreach (var code in TenantDefaults.RolePermissions[role.Code])
                    role.RolePermissions.Add(new RolePermission { Role = role, PermissionId = permissions[code].Id });
            var admin = new User { CompanyId = company.Id, EstablishmentId = establishment.Id, EmissionPointId = point.Id,
                Role = roles.Single(r => r.Code == AppRoles.Admin), Username = request.InitialAdmin.Username,
                Email = request.InitialAdmin.Email, IsActive = true, CreatedAt = now };
            admin.PasswordHash = new PasswordHasher<User>().HashPassword(admin, request.InitialAdmin.Password);
            database.Users.Add(admin);
            database.CompanySriSettings.Add(new CompanySriSettings { CompanyId = company.Id, Environment = 1,
                EmissionType = 1, IsEnabled = false, CertificateConfigured = false, CreatedAt = now });
            database.PlatformTenantEvents.Add(new PlatformTenantEvent { CompanyId = company.Id, PlatformUserId = actor.UserId,
                EventType = PlatformTenantEventType.Provisioned, RequestId = request.RequestId, CreatedAt = now,
                ProvisioningSnapshot = snapshot, InitialAdminUser = admin });
            await database.SaveChangesAsync();
            await transaction.CommitAsync();
            return new(await GetByIdAsync(company.Id), false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
            && pg.ConstraintName is "IX_Companies_Ruc" or "IX_Users_Username" or "IX_Users_Email" or "IX_PlatformTenantEvents_RequestId")
        {
            database.ChangeTracker.Clear();
            var prior = await ReplayAsync(actor, request, snapshot);
            if (prior is not null) return prior;
            throw new PlatformException(pg.ConstraintName switch
            {
                "IX_Companies_Ruc" => "TENANT_RUC_ALREADY_EXISTS",
                "IX_Users_Username" => "USERNAME_ALREADY_EXISTS",
                "IX_Users_Email" => "EMAIL_ALREADY_EXISTS",
                _ => "TENANT_PROVISIONING_REQUEST_CONFLICT"
            }, 409);
        }
    }

    public async Task<PlatformTenantDetailDto> SetActiveAsync(int id, bool active, TenantLifecycleRequest request)
    {
        var actor = await accessor.GetRequiredContextAsync();
        await using (var transaction = await guard.BeginChangeAsync(id))
        {
            // Company -> PlatformUser; provisioning never locks an existing Company after PlatformUser.
            await LockPlatformActorAsync(actor);
            var company = await database.Companies.SingleOrDefaultAsync(c => c.Id == id)
                ?? throw new PlatformException("TENANT_NOT_FOUND", 404);
            if (company.IsActive != active)
            {
                var reason = request.Reason?.Trim();
                if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new PlatformException("TENANT_REASON_REQUIRED");
                company.IsActive = active;
                if (!active) await database.Users.Where(u => u.CompanyId == id)
                    .ExecuteUpdateAsync(updates => updates.SetProperty(u => u.SessionVersion, u => u.SessionVersion + 1));
                database.PlatformTenantEvents.Add(new PlatformTenantEvent { CompanyId = id, PlatformUserId = actor.UserId,
                    EventType = active ? PlatformTenantEventType.Reactivated : PlatformTenantEventType.Suspended,
                    Reason = reason, CreatedAt = clock.UtcNow });
                await database.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        return await GetByIdAsync(id);
    }

    private async Task LockPlatformActorAsync(PlatformContext actor)
    {
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"PlatformUsers\" WHERE \"Id\" = {actor.UserId} FOR SHARE");
        await PlatformContextAccessor.RevalidateAsync(database, actor);
    }

    private async Task<TenantProvisionResult?> ReplayAsync(PlatformContext actor, TenantProvisionRequest request, string snapshot)
    {
        var prior = await database.PlatformTenantEvents.AsNoTracking().Where(e => e.RequestId == request.RequestId)
            .Select(e => new { e.CompanyId, e.PlatformUserId, e.ProvisioningSnapshot,
                e.InitialAdminUser!.PasswordHash }).SingleOrDefaultAsync();
        if (prior is null) return null;
        if (prior.PlatformUserId != actor.UserId || prior.ProvisioningSnapshot != snapshot
            || new PasswordHasher<User>().VerifyHashedPassword(new User(), prior.PasswordHash, request.InitialAdmin.Password)
                == PasswordVerificationResult.Failed)
            throw new PlatformException("TENANT_PROVISIONING_REQUEST_CONFLICT", 409);
        return new(await GetByIdAsync(prior.CompanyId), true);
    }

    private TenantProvisionRequest Normalize(TenantProvisionRequest request)
    {
        static string Required(string? text, int max)
        {
            var value = text?.Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new PlatformException("TENANT_PROVISIONING_INVALID");
            return value;
        }
        if (request is null || request.RequestId == Guid.Empty || request.Company is null
            || request.InitialEstablishment is null || request.InitialEmissionPoint is null || request.InitialAdmin is null)
            throw new PlatformException("TENANT_PROVISIONING_INVALID");
        var name = Required(request.Company.Name, 150);
        var ruc = Required(request.Company.Ruc, 13);
        if (ruc.Length != 13 || ruc.Any(c => c < '0' || c > '9')) throw new PlatformException("INVALID_COMPANY_RUC");
        var zone = Required(request.Company.TimeZoneId, 100);
        clock.ResolveTimeZone(zone);
        var username = Required(request.InitialAdmin.Username, 100);
        var email = Required(request.InitialAdmin.Email, 320);
        if (!PlatformAuthService.ValidIdentity(username, email, request.InitialAdmin.Password))
            throw new PlatformException("TENANT_ADMIN_INVALID");
        var address = string.IsNullOrWhiteSpace(request.InitialEstablishment.Address) ? "N/A" : Required(request.InitialEstablishment.Address, 250);
        return request with { Company = new(name, ruc, zone), InitialEstablishment = new(Required(request.InitialEstablishment.Name, 150), address),
            InitialEmissionPoint = new(Required(request.InitialEmissionPoint.Name, 150)),
            InitialAdmin = new(username, email, request.InitialAdmin.Password) };
    }
}
