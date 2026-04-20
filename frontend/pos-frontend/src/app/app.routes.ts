import { Routes } from '@angular/router';
import { AuthGuard } from './core/guards/auth.guard';
import { administrationAccessGuard } from './core/guards/administration-access.guard';
import { catalogAccessGuard } from './core/guards/catalog-access.guard';
import { inventoryAccessGuard } from './core/guards/inventory-access.guard';
import { operationalStructureAccessGuard } from './core/guards/operational-structure-access.guard';
import { posAccessGuard } from './core/guards/pos-access.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./modules/auth/login/login').then((m) => m.Login),
  },
  {
    path: 'dashboard',
    canActivate: [AuthGuard],
    loadComponent: () => import('./modules/dashboard/dashboard').then((m) => m.Dashboard),
  },
  {
    path: 'catalog',
    canActivate: [AuthGuard, catalogAccessGuard],
    loadComponent: () => import('./features/catalog/pages/catalog-page/catalog-page').then((m) => m.CatalogPage),
  },
  {
    path: 'operational-structure',
    canActivate: [AuthGuard, operationalStructureAccessGuard],
    loadComponent: () =>
      import('./features/operational-structure/pages/operational-structure-page/operational-structure-page').then(
        (m) => m.OperationalStructurePage
      ),
  },
  {
    path: 'administration',
    canActivate: [AuthGuard, administrationAccessGuard],
    loadComponent: () =>
      import('./features/administration/pages/administration-page/administration-page').then((m) => m.AdministrationPage),
  },
  {
    path: 'pos',
    canActivate: [AuthGuard, posAccessGuard],
    loadComponent: () => import('./features/pos-workstation/pages/pos-workstation-page/pos-workstation-page').then((m) => m.PosWorkstationPage),
  },
  {
    path: 'inventory',
    canActivate: [AuthGuard, inventoryAccessGuard],
    loadComponent: () => import('./features/inventory/pages/inventory-page/inventory-page').then((m) => m.InventoryPage),
  },
  {
    path: '',
    redirectTo: 'login',
    pathMatch: 'full',
  },
  {
    path: '**',
    redirectTo: 'login',
  },
];
