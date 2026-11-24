import { ApplicationConfig, inject, provideEnvironmentInitializer, provideBrowserGlobalErrorListeners, isDevMode } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideServiceWorker } from '@angular/service-worker';
import { SUPABASE_CLIENT, createSupabaseClient } from './core/auth/supabase-client';
import { apiTokenInterceptor } from './core/auth/token.interceptor';
import { SteamLinkFlow } from './features/manage/steam/steam-link-flow';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withInterceptors([apiTokenInterceptor])),
    provideRouter(routes),
    { provide: SUPABASE_CLIENT, useFactory: createSupabaseClient },
    // Observe logout/subject changes even away from the Steam feature route.
    provideEnvironmentInitializer(() => { inject(SteamLinkFlow); }),
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
