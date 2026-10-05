import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '../stores/auth.store';
import { PlatformStore } from '../../modules/platform/platform.store';
import { environment } from '../../../environments/environment';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(AuthStore);
  const platformStore = inject(PlatformStore);
  const router = inject(Router);

  const url = new URL(req.url, location.origin);
  const path = url.pathname.toLowerCase();
  const isApi = path.startsWith('/api/') && ((req.url.startsWith('/') && url.origin === location.origin)
    || url.origin === new URL(environment.apiUrl, location.origin).origin);
  const platform = isApi && (path === '/api/platform' || path.startsWith('/api/platform/'));
  const token = platform ? platformStore.token() : store.token();
  const publicRecovery = path === '/api/account/recovery/complete' || path === '/api/platform/account/recovery/complete';

  const authReq = isApi ? req.clone({ headers: token && !publicRecovery
    ? req.headers.set('Authorization', `Bearer ${token}`)
    : req.headers.delete('Authorization') }) : req;

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (isApi && error instanceof HttpErrorResponse && error.status === 401 && !isLoginRequest(req.url) && !publicRecovery) {
        if (platform) {
          platformStore.clear();
          if (!router.url.startsWith('/platform/login')) router.navigate(['/platform/login']);
        } else {
          store.clear();

          if (!router.url.startsWith('/login')) {
            router.navigate(['/login'], {
              queryParams: { message: 'session-expired' },
            });
          }
        }
      }

      return throwError(() => error);
    })
  );
};

function isLoginRequest(url: string): boolean {
  const path = new URL(url, location.origin).pathname.toLowerCase();
  return path === '/api/auth/login' || path === '/api/platform/auth/login';
}
