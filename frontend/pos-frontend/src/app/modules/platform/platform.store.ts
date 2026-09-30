import { computed, inject, Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { catchError, of, tap } from 'rxjs';
import { PlatformApi } from './platform-api.service';
import { PlatformMe } from './platform.model';

@Injectable({ providedIn: 'root' })
export class PlatformStore {
  private readonly api = inject(PlatformApi);
  readonly token = signal<string | null>(localStorage.getItem('platform_token'));
  readonly me = signal<PlatformMe | null>(null);
  readonly authenticated = computed(() => !!this.token() && this.me()?.role === 'PLATFORM_ADMIN');
  setToken(token: string) { localStorage.setItem('platform_token', token); this.token.set(token); this.me.set(null); }
  loadMe() {
    return this.api.me().pipe(tap(me => this.me.set(me)), catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) this.clear();
      return of(null);
    }));
  }
  clear() { localStorage.removeItem('platform_token'); this.token.set(null); this.me.set(null); }
}
