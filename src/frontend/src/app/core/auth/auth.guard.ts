import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.initialize();

  if (auth.isAuthenticated()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};

export const guestGuard: CanActivateFn = async (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.initialize();

  // Presentation only, for this navigation: unlike history state, info is not
  // restored by Back/Forward or reload. Never replay the completion exception.
  // This changes neither the normal session nor password-update authority.
  if (route.routeConfig?.path === 'login'
    && router.currentNavigation()?.extras.info === 'passwordResetComplete') return true;

  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/']);
  }
  return true;
};
