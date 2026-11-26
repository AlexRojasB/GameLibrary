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

  // Presentation only: recovery completion can show Sign in without signing out
  // an unrelated normal account. This flag grants no password-update authority.
  if (route.routeConfig?.path === 'login'
    && router.currentNavigation()?.extras.state?.['passwordResetComplete'] === true) return true;

  if (auth.isAuthenticated()) {
    return router.createUrlTree(['/']);
  }
  return true;
};
