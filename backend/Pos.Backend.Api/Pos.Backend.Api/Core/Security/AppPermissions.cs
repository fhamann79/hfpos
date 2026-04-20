namespace Pos.Backend.Api.Core.Security;

public static class AppPermissions
{
    public const string AuthProbeAdmin = "AUTH_PROBE_ADMIN";
    public const string AuthProbeSupervisor = "AUTH_PROBE_SUPERVISOR";
    public const string AuthProbeCashier = "AUTH_PROBE_CASHIER";
    public const string CatalogCategoriesRead = "CATALOG_CATEGORIES_READ";
    public const string CatalogCategoriesWrite = "CATALOG_CATEGORIES_WRITE";
    public const string CatalogProductsRead = "CATALOG_PRODUCTS_READ";
    public const string CatalogProductsWrite = "CATALOG_PRODUCTS_WRITE";
    public const string OpStructureRead = "OP_STRUCTURE_READ";
    public const string OpStructureWrite = "OP_STRUCTURE_WRITE";
    public const string PosSalesCreate = "POS_SALES_CREATE";
    public const string InventoryRead = "INVENTORY_READ";
    public const string InventoryWrite = "INVENTORY_WRITE";
    public const string PosSalesVoid = "POS_SALES_VOID";
    public const string ReportsSalesRead = "REPORTS_SALES_READ";
    public const string AdminUsersRead = "ADMIN_USERS_READ";
    public const string AdminUsersWrite = "ADMIN_USERS_WRITE";
    public const string AdminRolesRead = "ADMIN_ROLES_READ";
    public const string AdminRolesWrite = "ADMIN_ROLES_WRITE";
}
