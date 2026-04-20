using Microsoft.AspNetCore.Authorization;

namespace Pos.Backend.Api.Core.Security;

public static class AuthorizationOptionsExtensions
{
    public static void AddPermissionPolicies(this AuthorizationOptions options, IEnumerable<string> permissions)
    {
        foreach (var permission in permissions)
        {
            options.AddPolicy($"Perm:{permission}", policy =>
                policy.RequireClaim(AppClaims.Permission, permission));

            options.AddPolicy(permission, policy =>
                policy.RequireClaim(AppClaims.Permission, permission));
        }
    }
}
