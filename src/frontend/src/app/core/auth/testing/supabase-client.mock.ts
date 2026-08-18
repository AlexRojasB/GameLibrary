import { Session, SupabaseClient } from '@supabase/supabase-js';
import { vi } from 'vitest';

export function createTestSession(overrides: Partial<Session> = {}): Session {
  const user = {
    id: 'user-1',
    aud: 'authenticated',
    role: 'authenticated',
    email: 'user@example.com',
    created_at: '2026-01-01T00:00:00Z',
    updated_at: '2026-01-01T00:00:00Z',
  };
  return {
    access_token: 'test-access-token',
    refresh_token: 'test-refresh-token',
    expires_in: 3600,
    expires_at: Math.floor(Date.now() / 1000) + 3600,
    token_type: 'bearer',
    user,
    ...overrides,
  } as Session;
}

export interface MockSupabaseAuth {
  getSession: ReturnType<typeof vi.fn>;
  onAuthStateChange: ReturnType<typeof vi.fn>;
  signInWithPassword: ReturnType<typeof vi.fn>;
  signUp: ReturnType<typeof vi.fn>;
  signOut: ReturnType<typeof vi.fn>;
}

export function createMockSupabase(initialSession: Session | null = null): {
  client: SupabaseClient;
  auth: MockSupabaseAuth;
  emit: (event: string, session: Session | null) => void;
} {
  const authListeners: ((event: string, session: Session | null) => void)[] = [];

  const auth: MockSupabaseAuth = {
    getSession: vi.fn(async () => ({ data: { session: initialSession }, error: null })),
    onAuthStateChange: vi.fn((callback: (event: string, session: Session | null) => void) => {
      authListeners.push(callback);
      return { data: { subscription: { unsubscribe: vi.fn() } } };
    }),
    signInWithPassword: vi.fn(),
    signUp: vi.fn(),
    signOut: vi.fn(),
  };

  return {
    client: { auth } as unknown as SupabaseClient,
    auth,
    emit: (event: string, session: Session | null) => authListeners.forEach((callback) => callback(event, session)),
  };
}