import type { Session } from '@supabase/supabase-js';

// Presentation/lifetime identity only, never authorization. Supabase preserves
// session_id across token refreshes and assigns a new one for a new login.
export function coverSessionKey(session: Session | null): string | Session | null {
  if (session === null) return null;
  try {
    const encoded = session.access_token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const claims = JSON.parse(atob(encoded.padEnd(Math.ceil(encoded.length / 4) * 4, '=')));
    if (typeof claims.session_id === 'string' && /^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(claims.session_id)) {
      return `${session.user.id}:${claims.session_id.toLowerCase()}`;
    }
  } catch { /* Custom development JWTs have no Supabase session_id. */ }
  return session;
}
