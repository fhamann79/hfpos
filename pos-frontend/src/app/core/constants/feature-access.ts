import { PERMISSIONS } from './permissions';

export type PermissionMatchMode = 'any' | 'all';

export interface PermissionRequirement {
  requiredPermissions?: string[];
  matchMode?: PermissionMatchMode;
}

export interface NavigationItemConfig extends PermissionRequirement {
  label: string;
  icon: string;
  route: string | null;
  disabled?: boolean;
}

export const CATALOG_ACCESS_REQUIREMENT: PermissionRequirement = {
  requiredPermissions: [PERMISSIONS.catalogCategoriesRead, PERMISSIONS.catalogProductsRead],
  matchMode: 'any',
};

export const OPERATIONAL_STRUCTURE_ACCESS_REQUIREMENT: PermissionRequirement = {
  requiredPermissions: [PERMISSIONS.operationalStructureRead],
  matchMode: 'all',
};

export const ADMINISTRATION_ACCESS_REQUIREMENT: PermissionRequirement = {
  requiredPermissions: [PERMISSIONS.adminUsersRead, PERMISSIONS.adminRolesRead],
  matchMode: 'any',
};

export const POS_ACCESS_REQUIREMENT: PermissionRequirement = {
  requiredPermissions: [PERMISSIONS.posSalesCreate, PERMISSIONS.reportsSalesRead],
  matchMode: 'any',
};

export const NAVIGATION_ITEMS: NavigationItemConfig[] = [
  {
    label: 'Dashboard',
    icon: 'pi pi-chart-line',
    route: '/dashboard',
  },
  {
    label: 'Catálogo',
    icon: 'pi pi-box',
    route: '/catalog',
    ...CATALOG_ACCESS_REQUIREMENT,
  },
  {
    label: 'Estructura Operativa',
    icon: 'pi pi-sitemap',
    route: '/operational-structure',
    ...OPERATIONAL_STRUCTURE_ACCESS_REQUIREMENT,
  },
  {
    label: 'Administración',
    icon: 'pi pi-shield',
    route: '/administration',
    ...ADMINISTRATION_ACCESS_REQUIREMENT,
  },
  {
    label: 'POS',
    icon: 'pi pi-shopping-cart',
    route: '/pos',
    ...POS_ACCESS_REQUIREMENT,
  },
];
