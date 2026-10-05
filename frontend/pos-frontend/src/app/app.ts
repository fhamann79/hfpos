import { Component, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { AppShell } from './shared/components/app-shell/app-shell';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, AppShell],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App {
  private readonly router = inject(Router);
  private readonly currentUrl = signal(this.router.url);

  readonly showShell = computed(() => {
    const path = this.currentUrl().split(/[?#]/, 1)[0];
    return path !== '/login' && path !== '/recover-access' && path !== '/recover-access/complete'
      && !path.startsWith('/platform/');
  });

  constructor() {
    this.router.events.subscribe((event) => {
      if (event instanceof NavigationEnd) {
        this.currentUrl.set(event.urlAfterRedirects);
      }
    });
  }
}
