import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';
import { ToolbarModule } from 'primeng/toolbar';
import { ButtonModule } from 'primeng/button';
import { AvatarModule } from 'primeng/avatar';
import { AuthStore } from '../../../core/stores/auth.store';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    RouterLinkActive,
    ToolbarModule,
    ButtonModule,
    AvatarModule,
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.scss',
})
export class AppShell {
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);

  readonly sidebarCollapsed = signal(false);
  readonly me = this.store.me;
  readonly username = computed(() => this.me()?.username ?? 'Usuario');

  readonly menuItems = [
    { label: 'Dashboard', icon: 'pi pi-chart-line', route: '/dashboard', disabled: false },
    { label: 'Catálogo', icon: 'pi pi-box', route: '/catalog', disabled: false },
    { label: 'Ventas', icon: 'pi pi-shopping-cart', route: null, disabled: true },
  ];

  toggleSidebar(): void {
    this.sidebarCollapsed.update((value) => !value);
  }

  logout(): void {
    this.store.clear();
    this.router.navigateByUrl('/login');
  }
}
