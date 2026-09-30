namespace Pos.Backend.Api.Core.Security;

public sealed record PermissionDefault(string Code, string Description);
public sealed record RoleDefault(string Code, string Name);

public static class TenantDefaults
{
        public static readonly PermissionDefault[] Permissions =
        {
            new(AppPermissions.AuthProbeAdmin, "Acceso a prueba de autorización admin"),
            new(AppPermissions.AuthProbeSupervisor, "Acceso a prueba de autorización supervisor"),
            new(AppPermissions.AuthProbeCashier, "Acceso a prueba de autorización cajero"),
            new(AppPermissions.CatalogCategoriesRead, "Read categories"),
            new(AppPermissions.CatalogCategoriesWrite, "Write categories"),
            new(AppPermissions.CatalogProductsRead, "Read products"),
            new(AppPermissions.CatalogProductsWrite, "Write products"),
            new(AppPermissions.CustomersRead, "Leer clientes"),
            new(AppPermissions.CustomersWrite, "Escribir clientes"),
            new(AppPermissions.SuppliersRead, "Leer proveedores"),
            new(AppPermissions.SuppliersWrite, "Escribir proveedores"),
            new(AppPermissions.PurchasesRead, "Leer recepciones de compra"),
            new(AppPermissions.PurchasesWrite, "Registrar recepciones de compra"),
            new(AppPermissions.CashSessionsRead, "Leer cajas"),
            new(AppPermissions.CashSessionsWrite, "Administrar cajas"),
            new(AppPermissions.OpStructureRead, "Read operational structure"),
            new(AppPermissions.OpStructureWrite, "Write operational structure"),
            new(AppPermissions.PosSalesCreate, "Crear ventas POS"),
            new(AppPermissions.InventoryRead, "Leer inventario"),
            new(AppPermissions.InventoryWrite, "Escribir inventario"),
            new(AppPermissions.PosSalesVoid, "Anular ventas POS"),
            new(AppPermissions.ReportsSalesRead, "Leer reportes de ventas"),
            new(AppPermissions.SriDocumentsSign, "Firmar documentos electrónicos SRI"),
            new(AppPermissions.SriDocumentsSubmit, "Enviar y consultar documentos electrónicos SRI"),
            new(AppPermissions.FiscalSettingsRead, "Leer configuración fiscal y empresarial"),
            new(AppPermissions.FiscalSettingsWrite, "Escribir configuración fiscal y empresarial"),
            new(AppPermissions.AdminUsersRead, "Leer administración de usuarios"),
            new(AppPermissions.AdminUsersWrite, "Escribir administración de usuarios"),
            new(AppPermissions.AdminRolesRead, "Leer administración de roles"),
            new(AppPermissions.AdminRolesWrite, "Escribir administración de roles")
        };

        public static readonly RoleDefault[] Roles =
        {
            new(AppRoles.Admin, "Administrador"),
            new(AppRoles.Supervisor, "Supervisor"),
            new(AppRoles.Cashier, "Cajero")
        };

        public static readonly IReadOnlyDictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
        {
            {
                AppRoles.Admin,
                new[]
                {
                    AppPermissions.AuthProbeAdmin,
                    AppPermissions.AuthProbeSupervisor,
                    AppPermissions.AuthProbeCashier,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogCategoriesWrite,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.CatalogProductsWrite,
                    AppPermissions.CustomersRead,
                    AppPermissions.CustomersWrite,
                    AppPermissions.SuppliersRead,
                    AppPermissions.SuppliersWrite,
                    AppPermissions.PurchasesRead,
                    AppPermissions.PurchasesWrite,
                    AppPermissions.CashSessionsRead,
                    AppPermissions.CashSessionsWrite,
                    AppPermissions.OpStructureRead,
                    AppPermissions.OpStructureWrite,
                    AppPermissions.PosSalesCreate,
                    AppPermissions.PosSalesVoid,
                    AppPermissions.InventoryRead,
                    AppPermissions.InventoryWrite,
                    AppPermissions.ReportsSalesRead,
                    AppPermissions.SriDocumentsSign,
                    AppPermissions.SriDocumentsSubmit,
                    AppPermissions.FiscalSettingsRead,
                    AppPermissions.FiscalSettingsWrite,
                    AppPermissions.AdminUsersRead,
                    AppPermissions.AdminUsersWrite,
                    AppPermissions.AdminRolesRead,
                    AppPermissions.AdminRolesWrite
                }
            },
            {
                AppRoles.Supervisor,
                new[]
                {
                    AppPermissions.AuthProbeSupervisor,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogCategoriesWrite,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.CatalogProductsWrite,
                    AppPermissions.CustomersRead,
                    AppPermissions.CustomersWrite,
                    AppPermissions.SuppliersRead,
                    AppPermissions.SuppliersWrite,
                    AppPermissions.PurchasesRead,
                    AppPermissions.PurchasesWrite,
                    AppPermissions.CashSessionsRead,
                    AppPermissions.CashSessionsWrite,
                    AppPermissions.OpStructureRead,
                    AppPermissions.OpStructureWrite,
                    AppPermissions.PosSalesCreate,
                    AppPermissions.PosSalesVoid,
                    AppPermissions.InventoryRead,
                    AppPermissions.InventoryWrite,
                    AppPermissions.ReportsSalesRead,
                    AppPermissions.SriDocumentsSign,
                    AppPermissions.SriDocumentsSubmit,
                    AppPermissions.FiscalSettingsRead,
                    AppPermissions.AdminUsersRead,
                    AppPermissions.AdminUsersWrite,
                    AppPermissions.AdminRolesRead,
                    AppPermissions.AdminRolesWrite
                }
            },
            {
                AppRoles.Cashier,
                new[]
                {
                    AppPermissions.AuthProbeCashier,
                    AppPermissions.CatalogCategoriesRead,
                    AppPermissions.CatalogProductsRead,
                    AppPermissions.CustomersRead,
                    AppPermissions.CustomersWrite,
                    AppPermissions.CashSessionsRead,
                    AppPermissions.CashSessionsWrite,
                    AppPermissions.OpStructureRead,
                    AppPermissions.PosSalesCreate,
                    AppPermissions.InventoryRead
                }
            }
        };

}
