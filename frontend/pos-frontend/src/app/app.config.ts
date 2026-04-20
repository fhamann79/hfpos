import { ApplicationConfig, inject, provideAppInitializer } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient, withInterceptors } from '@angular/common/http';

import { routes } from './app.routes';
import { AuthStore } from './core/stores/auth.store';
import { authInterceptor } from './core/interceptors/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideRouter(routes),
    provideAnimations(),
    provideHttpClient(withInterceptors([authInterceptor])),

    provideAppInitializer(() => {
      const store = inject(AuthStore);

      if (store.hasSessionToken()) {
        return store.loadMe();
      }

      store.markLoaded();
      return;
    }),
  ],
};
