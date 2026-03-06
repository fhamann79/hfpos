import { Routes } from '@angular/router';
import { AuthGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./modules/auth/login/login').then(m => m.Login)
  },
  {
    path: 'dashboard',
    canActivate: [AuthGuard],
    loadComponent: () =>
      import('./modules/dashboard/dashboard').then(m => m.Dashboard)
  },

  {
    path: 'catalog',
    canActivate: [AuthGuard],
    loadComponent: () =>
      import('./features/catalog/pages/catalog-page/catalog-page').then(m => m.CatalogPage)
  },
  {
    path: '',
    redirectTo: 'login',
    pathMatch: 'full'
  },
  {
    path: '**',
    redirectTo: 'login'
  }
];
