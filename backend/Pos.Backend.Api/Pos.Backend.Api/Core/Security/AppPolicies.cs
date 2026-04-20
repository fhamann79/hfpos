namespace Pos.Backend.Api.Core.Security;

public static class AppPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string SupervisorOrAdmin = "SupervisorOrAdmin";
    public const string CashierOrAbove = "CashierOrAbove";
}
