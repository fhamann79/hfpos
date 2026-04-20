import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthStore } from '../stores/auth.store';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const store = inject(AuthStore);
  const router = inject(Router);

  const token = store.token();

  const authReq = token
    ? req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`,
        },
      })
    : req;

  return next(authReq).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isLoginRequest(req.url)) {
        store.clear();

        if (!router.url.startsWith('/login')) {
          router.navigate(['/login'], {
            queryParams: { message: 'session-expired' },
          });
        }
      }

      return throwError(() => error);
    })
  );
};

function isLoginRequest(url: string): boolean {
  return url.includes('/api/auth/login');
}
