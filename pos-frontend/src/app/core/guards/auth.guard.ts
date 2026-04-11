import { Injectable, inject } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthStore } from '../stores/auth.store';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  private readonly store = inject(AuthStore);
  private readonly router = inject(Router);

  canActivate(): boolean {
    if (this.store.isAuthenticated()) {
      return true;
    }

    this.router.navigate(['/login']);
    return false;
  }
}
