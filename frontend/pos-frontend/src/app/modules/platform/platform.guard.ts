import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { PlatformStore } from './platform.store';

export const platformGuard: CanActivateFn = () => {
  const store = inject(PlatformStore);
  const router = inject(Router);
  const login = router.createUrlTree(['/platform/login']);
  if (!store.token()) return login;
  if (store.authenticated()) return true;
  return store.loadMe().pipe(map(me => me?.role === 'PLATFORM_ADMIN' ? true : login));
};
