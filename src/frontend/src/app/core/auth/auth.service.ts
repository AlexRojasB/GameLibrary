import { computed, inject, Injectable, signal } from '@angular/core';
import { Session } from '@supabase/supabase-js';

import { environment } from '../../../environments/environment';

import { SUPABASE_CLIENT } from './supabase-client';

export class AuthServiceError extends Error {}

export interface SignUpResult {
  needsEmailConfirmation: boolean;
}

const e2eStorageKey = 'game-library-e2e-session';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly supabase = inject(SUPABASE_CLIENT);

  private readonly authSession = signal<Session | null>(null);
  private initPromise: Promise<void> | null = null;

  readonly session = this.authSession.asReadonly();
  readonly isAuthenticated = computed(() => this.authSession() !== null);
  readonly accessToken = computed(() => this.authSession()?.access_token ?? null);

  constructor() {
    void this.initialize();
  }

  initialize(): Promise<void> {
    this.initPromise ??= this.doInitialize();
    return this.initPromise;
  }

  async signIn(email: string, password: string): Promise<void> {
    if (environment.e2e.enabled) {
      await this.signInE2e(email, password);
      return;
    }

    try {
      const { data, error } = await this.supabase.auth.signInWithPassword({ email, password });
      if (error) {
        throw error;
      }
      this.authSession.set(data.session);
    } catch (error) {
      throw new AuthServiceError(normalizeAuthError(error));
    }
  }

  async signUp(email: string, password: string): Promise<SignUpResult> {
    try {
      const { data, error } = await this.supabase.auth.signUp({ email, password });
      if (error) {
        throw error;
      }
      return { needsEmailConfirmation: data.session === null };
    } catch (error) {
      throw new AuthServiceError(normalizeAuthError(error));
    }
  }

  async signOut(): Promise<void> {
    if (environment.e2e.enabled) {
      this.authSession.set(null);
      localStorage.removeItem(e2eStorageKey);
      return;
    }

    try {
      const { error } = await this.supabase.auth.signOut();
      if (error) {
        throw error;
      }
    } finally {
      this.authSession.set(null);
    }
  }

  clearLocalSession(): void {
    this.authSession.set(null);
    if (environment.e2e.enabled) {
      localStorage.removeItem(e2eStorageKey);
    }
  }

  private async doInitialize(): Promise<void> {
    if (environment.e2e.enabled) {
      this.authSession.set(readE2eSession());
      return;
    }

    try {
      this.supabase.auth.onAuthStateChange((_event, session) => {
        this.authSession.set(session);
      });
      const { data } = await this.supabase.auth.getSession();
      this.authSession.set(data.session);
    } catch {
      this.authSession.set(null);
    }
  }

  private async signInE2e(email: string, password: string): Promise<void> {
    try {
      const response = await fetch(`${environment.apiBaseUrl}/e2e/auth/session`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });
      if (!response.ok) {
        throw new Error(`E2E auth failed with ${response.status}.`);
      }
      const body = (await response.json()) as E2eSessionResponse;
      const session = makeE2eSession(body);
      localStorage.setItem(e2eStorageKey, JSON.stringify(session));
      this.authSession.set(session);
    } catch (error) {
      throw new AuthServiceError(normalizeAuthError(error));
    }
  }
}

interface E2eSessionResponse {
  accessToken: string;
  userId: string;
  email: string;
  expiresAt: number;
}

function readE2eSession(): Session | null {
  const raw = localStorage.getItem(e2eStorageKey);
  if (raw === null) {
    return null;
  }
  try {
    return JSON.parse(raw) as Session;
  } catch {
    localStorage.removeItem(e2eStorageKey);
    return null;
  }
}

function makeE2eSession(response: E2eSessionResponse): Session {
  return {
    access_token: response.accessToken,
    refresh_token: 'e2e-refresh-token',
    expires_in: Math.max(response.expiresAt - Math.floor(Date.now() / 1000), 0),
    expires_at: response.expiresAt,
    token_type: 'bearer',
    user: {
      id: response.userId,
      app_metadata: {},
      user_metadata: {},
      aud: 'authenticated',
      created_at: new Date(0).toISOString(),
      email: response.email,
    },
  } as Session;
}

function normalizeAuthError(error: unknown): string {
  const message = errorMessage(error).toLowerCase();

  if (message.includes('invalid login credentials') || message.includes('invalid email or password')) {
    return 'Invalid email or password.';
  }
  if (message.includes('already registered') || message.includes('already exists')) {
    return 'An account with this email already exists.';
  }
  if (message.includes('password should be') || message.includes('password must be') || message.includes('weak password')) {
    return 'Password must be at least 6 characters.';
  }
  if (message.includes('failed to fetch') || message.includes('network') || message.includes('unreachable')) {
    return 'Unable to reach the authentication service. Check your connection and try again.';
  }
  return 'Unable to complete that action. Please try again.';
}

function errorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message;
  }
  if (typeof error === 'object' && error !== null && 'message' in error) {
    const message = (error as { message?: unknown }).message;
    if (typeof message === 'string') {
      return message;
    }
  }
  return String(error ?? '');
}
