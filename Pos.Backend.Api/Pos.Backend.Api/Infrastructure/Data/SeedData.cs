using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Pos.Backend.Api.Core.Entities;
using Pos.Backend.Api.Core.Security;

namespace Pos.Backend.Api.Infrastructure.Data;

public static class SeedData
{
    public static async Task SeedDevelopmentAsync(PosDbContext context)
    {
        const string companyRuc = "9999999999001";
        const string companyName = "Demo Company";
        const string establishmentCode = "001";
        const string emissionPointCode = "001";
        const string adminUsername = "admin";
        const string adminEmail = "admin@demo.local";
        const string adminPassword = "admin123";
        const string superUsername = "super";
        const string superEmail = "super@demo.local";
        const string superPassword = "super123";
        const string cashierUsername = "cashier";
        const string cashierEmail = "cashier@demo.local";
        const string cashierPassword = "cashier123";

        var roleDefinitions = new[]
        {
            new { Code = AppRoles.Admin, Name = "Administrador" },
            new { Code = AppRoles.Supervisor, Name = "Supervisor" },
            new { Code = AppRoles.Cashier, Name = "Cajero" }
        };

        foreach (var roleDefinition in roleDefinitions)
        {
            var exists = await context.Roles
                .AnyAsync(r => r.Code == roleDefinition.Code);

            if (!exists)
            {
                context.Roles.Add(new Role
                {
                    Code = roleDefinition.Code,
                    Name = roleDefinition.Name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();

        var adminRole = await context.Roles
            .FirstAsync(r => r.Code == AppRoles.Admin);

        var supervisorRole = await context.Roles
            .FirstAsync(r => r.Code == AppRoles.Supervisor);

        var cashierRole = await context.Roles
            .FirstAsync(r => r.Code == AppRoles.Cashier);

        var permissionDefinitions = new[]
        {
            new { Code = AppPermissions.AuthProbeAdmin, Description = "Acceso a prueba de autorización admin" },
            new { Code = AppPermissions.AuthProbeSupervisor, Description = "Acceso a prueba de autorización supervisor" },
            new { Code = AppPermissions.AuthProbeCashier, Description = "Acceso a prueba de autorización cajero" },
            new { Code = AppPermissions.CatalogCategoriesRead, Description = "Read categories" },
            new { Code = AppPermissions.CatalogCategoriesWrite, Description = "Write categories" },
            new { Code = AppPermissions.CatalogProductsRead, Description = "Read products" },
            new { Code = AppPermissions.CatalogProductsWrite, Description = "Write products" },
            new { Code = AppPermissions.OpStructureRead, Description = "Read operational structure" },
            new { Code = AppPermissions.OpStructureWrite, Description = "Write operational structure" },
            new { Code = AppPermissions.PosSalesCreate, Description = "Crear ventas POS" },
            new { Code = AppPermissions.PosSalesVoid, Description = "Anular ventas POS" },
            new { Code = AppPermissions.ReportsSalesRead, Description = "Leer reportes de ventas" }
        };

        var existingPermissions = await context.Permissions
            .ToListAsync();

        var existingPermissionByCode = existingPermissions
            .ToDictionary(p => p.Code, p => p);

        foreach (var permissionDefinition in permissionDefinitions)
        {
            if (!existingPermissionByCode.TryGetValue(permissionDefinition.Code, out var permission))
            {
                context.Permissions.Add(new Permission
                {
                    Code = permissionDefinition.Code,
                    Description = permissionDefinition.Description,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });

                continue;
            }

            if (permission.Description != permissionDefinition.Description)
            {
                permission.Description = permissionDefinition.Description;
            }

            if (!permission.IsActive)
            {
                permission.IsActive = true;
            }
        }

        await context.SaveChangesAsync();

        var permissions = await context.Permissions
            .Where(p => permissionDefinitions.Select(d => d.Code).Contains(p.Code))
            .ToListAsync();

        var permissionByCode = permissions.ToDictionary(p => p.Code, p => p);

        var rolePermissionMap = new Dictionary<int, string[]>
        {
            {
                adminRole.Id,
                new[]
                {
                    AppPermissions.AuthProbeAdmin,
                    AppPermissions.AuthProbeSupervisor,
                    AppPermissions.AuthProbeCashier,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogCategoriesWrite,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.CatalogProductsWrite,
                    AppPermissions.OpStructureRead,
                    AppPermissions.OpStructureWrite,
                    AppPermissions.PosSalesCreate,
                    AppPermissions.PosSalesVoid,
                    AppPermissions.ReportsSalesRead
                }
            },
            {
                supervisorRole.Id,
                new[]
                {
                    AppPermissions.AuthProbeSupervisor,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogCategoriesWrite,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.CatalogProductsWrite,
                    AppPermissions.OpStructureRead,
                    AppPermissions.OpStructureWrite,
                    AppPermissions.PosSalesCreate,
                    AppPermissions.PosSalesVoid,
                    AppPermissions.ReportsSalesRead
                }
            },
            {
                cashierRole.Id,
                new[]
                {
                    AppPermissions.AuthProbeCashier,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.OpStructureRead,
                    AppPermissions.PosSalesCreate
                }
            }
        };

        var roleIds = rolePermissionMap.Keys.ToArray();
        var permissionIds = permissionByCode.Values.Select(p => p.Id).ToArray();

        var existingRolePermissions = await context.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId) && permissionIds.Contains(rp.PermissionId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync();

        var existingRolePermissionSet = existingRolePermissions
            .Select(rp => (rp.RoleId, rp.PermissionId))
            .ToHashSet();

        foreach (var roleEntry in rolePermissionMap)
        {
            foreach (var permissionCode in roleEntry.Value)
            {
                if (!permissionByCode.TryGetValue(permissionCode, out var permission))
                {
                    continue;
                }

                var key = (roleEntry.Key, permission.Id);
                if (existingRolePermissionSet.Contains(key))
                {
                    continue;
                }

                context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleEntry.Key,
                    PermissionId = permission.Id
                });
            }
        }

        await context.SaveChangesAsync();

        var company = await context.Companies
            .FirstOrDefaultAsync(c => c.Ruc == companyRuc);

        if (company is null)
        {
            company = new Company
            {
                Name = companyName,
                Ruc = companyRuc,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Companies.Add(company);
            await context.SaveChangesAsync();
        }

        var establishment = await context.Establishments
            .FirstOrDefaultAsync(e => e.CompanyId == company.Id && e.Code == establishmentCode);

        if (establishment is null)
        {
            establishment = new Establishment
            {
                CompanyId = company.Id,
                Code = establishmentCode,
                Name = "Matriz",
                Address = "Direccion principal",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Establishments.Add(establishment);
            await context.SaveChangesAsync();
        }

        var emissionPoint = await context.EmissionPoints
            .FirstOrDefaultAsync(e => e.EstablishmentId == establishment.Id && e.Code == emissionPointCode);

        if (emissionPoint is null)
        {
            emissionPoint = new EmissionPoint
            {
                EstablishmentId = establishment.Id,
                Code = emissionPointCode,
                Name = "Caja Principal",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.EmissionPoints.Add(emissionPoint);
            await context.SaveChangesAsync();
        }

        var hasher = new PasswordHasher<User>();

        async Task EnsureDemoUserAsync(string username, string email, string password, Role role)
        {
            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Username == username);

            if (user is null)
            {
                user = new User
                {
                    Username = username,
                    Email = email,
                    CompanyId = company.Id,
                    RoleId = role.Id,
                    EstablishmentId = establishment.Id,
                    EmissionPointId = emissionPoint.Id,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                user.PasswordHash = hasher.HashPassword(user, password);
                context.Users.Add(user);
                await context.SaveChangesAsync();
                return;
            }

            var needsUpdate = false;

            if (string.IsNullOrWhiteSpace(user.Email))
            {
                user.Email = email;
                needsUpdate = true;
            }

            if (user.CompanyId <= 0)
            {
                user.CompanyId = company.Id;
                needsUpdate = true;
            }

            if (user.RoleId != role.Id)
            {
                user.RoleId = role.Id;
                needsUpdate = true;
            }

            if (user.EstablishmentId is null)
            {
                user.EstablishmentId = establishment.Id;
                needsUpdate = true;
            }

            if (user.EmissionPointId <= 0)
            {
                user.EmissionPointId = emissionPoint.Id;
                needsUpdate = true;
            }

            if (!user.IsActive)
            {
                user.IsActive = true;
                needsUpdate = true;
            }

            if (needsUpdate)
            {
                await context.SaveChangesAsync();
            }
        }

        await EnsureDemoUserAsync(adminUsername, adminEmail, adminPassword, adminRole);
        await EnsureDemoUserAsync(superUsername, superEmail, superPassword, supervisorRole);
        await EnsureDemoUserAsync(cashierUsername, cashierEmail, cashierPassword, cashierRole);
    }
}
