import { InjectionToken } from '@angular/core';
import { createClient, SupabaseClient } from '@supabase/supabase-js';

import { environment } from '../../../environments/environment';

// Separate SDK-owned storage/locks/events: a recovery session never signs the
// application in or replaces the ordinary user's session, including on completion.
export const PASSWORD_RECOVERY_CLIENT = new InjectionToken<SupabaseClient>('PASSWORD_RECOVERY_CLIENT', {
  providedIn: 'root',
  factory: () => createClient(environment.supabaseUrl, environment.supabaseKey, {
    auth: {
      storageKey: 'game-library-password-recovery',
      flowType: 'pkce',
      detectSessionInUrl: false,
      autoRefreshToken: false,
      debug: false,
    },
  }),
});

// One-shot, transient handoff to the SDK, never router state or persistent app state.
export const RECOVERY_CODE = new InjectionToken<() => string | null>('RECOVERY_CODE', {
  providedIn: 'root', factory: () => () => null,
});

export function captureRecoveryCode(): () => string | null {
  const url = new URL(window.location.href);
  let code: string | null = null;
  let route = url.pathname;
  try { route = decodeURIComponent(route); } catch { /* A malformed path cannot authorize recovery. */ }
  // Angular recognizes encoded paths, trailing slashes and matrix parameters.
  // Scrub those recovery-route variants too, but authorize only the exact URL.
  if (route.split('/')[1]?.split(';')[0] === 'reset-password') {
    const codes = url.searchParams.getAll('code');
    if (url.pathname === '/reset-password' && !url.hash && codes.length === 1 && codes[0].trim()
      && [...url.searchParams.keys()].every(key => key === 'code')) code = codes[0];
    // Before router initialization, Auth SDK URL detection, or error reporting.
    window.history.replaceState(null, '', '/reset-password');
  }
  return () => { const value = code; code = null; return value; };
}

export function recoveryRedirect(): string {
  const { protocol, hostname, origin } = window.location;
  if (protocol !== 'https:' && !(protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]'].includes(hostname))) {
    throw new Error('Password recovery requires a secure origin.');
  }
  return origin + '/reset-password';
}

export function recoveryErrorStatus(error: unknown): number {
  return typeof error === 'object' && error !== null && 'status' in error && typeof error.status === 'number'
    ? error.status : 0;
}
