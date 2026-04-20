import { computed, inject, Injectable, signal } from '@angular/core';
import { AuthService } from '../services/auth';
import { MeResponse } from '../models/me';
import { catchError, of, tap } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class AuthStore {
  private auth = inject(AuthService);
  private readonly tokenKey = 'token';

  private _token = signal<string | null>(localStorage.getItem(this.tokenKey));
  private _me = signal<MeResponse | null>(null);
  private _loaded = signal(false);

  token = computed(() => this._token());
  me = computed(() => this._me());
  loaded = computed(() => this._loaded());
  hasSessionToken = computed(() => !!this._token());
  isAuthenticated = computed(() => !!this._token() && !!this._me());

  companyId = computed(() => this._me()?.companyId ?? null);
  establishmentId = computed(() => this._me()?.establishmentId ?? null);
  emissionPointId = computed(() => this._me()?.emissionPointId ?? null);
  username = computed(() => this._me()?.username ?? null);
  roleCode = computed(() => this._me()?.roleCode ?? null);
  permissions = computed(() => this._me()?.permissions ?? []);

  setToken(token: string) {
    localStorage.setItem(this.tokenKey, token);
    this._token.set(token);
  }

  markLoaded() {
    this._loaded.set(true);
  }

  loadMe() {
    this._loaded.set(false);

    return this.auth.me().pipe(
      tap((res) => {
        this._me.set(res);
        this.auth.saveContext(res);
        this._loaded.set(true);
      }),
      catchError(() => {
        this.clear();
        return of(null);
      })
    );
  }

  clear() {
    localStorage.removeItem(this.tokenKey);
    this._token.set(null);
    this.auth.clearContext();
    this._me.set(null);
    this._loaded.set(true);
  }
}
