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

        var company = await context.Companies
            .FirstOrDefaultAsync(c => c.Ruc == companyRuc);

        if (company is null)
        {
            company = new Company
            {
                Name = companyName,
                Ruc = companyRuc,
                TradeName = "HF POS Demo",
                MatrixAddress = "Direccion matriz demo",
                Email = "demo@example.com",
                Phone = "0999999999",
                TimeZoneId = "America/Guayaquil",
                IsAccountingRequired = false,
                TaxpayerRegime = "GENERAL",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            context.Companies.Add(company);
            await context.SaveChangesAsync();
        }
        else
        {
            var companyNeedsUpdate = false;

            if (string.IsNullOrWhiteSpace(company.MatrixAddress))
            {
                company.MatrixAddress = "Direccion matriz demo";
                companyNeedsUpdate = true;
            }

            if (string.IsNullOrWhiteSpace(company.TimeZoneId))
            {
                company.TimeZoneId = "America/Guayaquil";
                companyNeedsUpdate = true;
            }

            if (companyNeedsUpdate)
            {
                await context.SaveChangesAsync();
            }
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

        var permissionDefinitions = TenantDefaults.Permissions;

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

        var roleDefinitions = TenantDefaults.Roles;

        foreach (var roleDefinition in roleDefinitions)
        {
            var exists = await context.Roles
                .AnyAsync(r => r.CompanyId == company.Id && r.Code == roleDefinition.Code);

            if (!exists)
            {
                context.Roles.Add(new Role
                {
                    CompanyId = company.Id,
                    Code = roleDefinition.Code,
                    Name = roleDefinition.Name,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();

        var adminRole = await context.Roles
            .FirstAsync(r => r.CompanyId == company.Id && r.Code == AppRoles.Admin);

        var supervisorRole = await context.Roles
            .FirstAsync(r => r.CompanyId == company.Id && r.Code == AppRoles.Supervisor);

        var cashierRole = await context.Roles
            .FirstAsync(r => r.CompanyId == company.Id && r.Code == AppRoles.Cashier);

        var rolePermissionMap = new[] { adminRole, supervisorRole, cashierRole }
            .ToDictionary(role => role.Id, role => TenantDefaults.RolePermissions[role.Code]);

        var roleIds = rolePermissionMap.Keys.ToArray();
        var permissionIds = permissionByCode.Values.Select(p => p.Id).ToArray();

        var existingRolePermissions = await context.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId) && permissionIds.Contains(rp.PermissionId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync();

        var existingRolePermissionSet = existingRolePermissions
            .Select(rp => (rp.RoleId, rp.PermissionId))
            .ToHashSet();
        var changedRoleIds = new HashSet<int>();

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
                changedRoleIds.Add(roleEntry.Key);
            }
        }

        foreach (var role in new[] { adminRole, supervisorRole, cashierRole }
            .Where(r => changedRoleIds.Contains(r.Id)))
        {
            role.AuthorizationVersion = checked(role.AuthorizationVersion + 1);
        }
        await context.SaveChangesAsync();

        var sriSettings = await context.CompanySriSettings
            .FirstOrDefaultAsync(s => s.CompanyId == company.Id);

        if (sriSettings is null)
        {
            context.CompanySriSettings.Add(new CompanySriSettings
            {
                CompanyId = company.Id,
                Environment = 1,
                EmissionType = 1,
                IsEnabled = false,
                CertificateConfigured = false,
                CreatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        var hasher = new PasswordHasher<User>();

        async Task EnsureDemoUserAsync(string username, string email, string password, Role role)
        {
            // Demo identities never take ownership of an existing global username or email.
            if (await context.Users.AnyAsync(u => u.Username == username))
            {
                return;
            }

            if (await context.Users.AnyAsync(u => u.Email == email))
            {
                return;
            }

            var user = new User
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
        }

        await EnsureDemoUserAsync(adminUsername, adminEmail, adminPassword, adminRole);
        await EnsureDemoUserAsync(superUsername, superEmail, superPassword, supervisorRole);
        await EnsureDemoUserAsync(cashierUsername, cashierEmail, cashierPassword, cashierRole);
    }
}
