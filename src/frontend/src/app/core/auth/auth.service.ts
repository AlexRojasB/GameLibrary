import { computed, inject, Injectable, signal } from '@angular/core';
import { Session } from '@supabase/supabase-js';

import { SUPABASE_CLIENT } from './supabase-client';

export class AuthServiceError extends Error {}

export interface SignUpResult {
  needsEmailConfirmation: boolean;
}

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
  }

  private async doInitialize(): Promise<void> {
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