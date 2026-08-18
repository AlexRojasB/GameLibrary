import { TestBed } from '@angular/core/testing';

import { AuthService } from './auth.service';
import { SUPABASE_CLIENT } from './supabase-client';
import { createMockSupabase, createTestSession } from './testing/supabase-client.mock';

describe('AuthService', () => {
  it('restores a persisted session on initialize', async () => {
    const mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('test-access-token');
  });

  it('stays signed out when there is no persisted session', async () => {
    const mock = createMockSupabase(null);
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
  });

  it('signs in and sets the authenticated state', async () => {
    const mock = createMockSupabase();
    mock.auth.signInWithPassword.mockResolvedValue({
      data: { session: createTestSession(), user: createTestSession().user },
      error: null,
    });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    await auth.signIn('user@example.com', 'password');

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.accessToken()).toBe('test-access-token');
  });

  it('normalizes sign-in failures and stays signed out', async () => {
    const mock = createMockSupabase();
    mock.auth.signInWithPassword.mockResolvedValue({
      data: { session: null },
      error: { name: 'AuthApiError', message: 'Invalid login credentials', status: 400 },
    });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    await expect(auth.signIn('user@example.com', 'wrong')).rejects.toThrow('Invalid email or password.');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('signs up and reports when email confirmation is required', async () => {
    const mock = createMockSupabase();
    mock.auth.signUp.mockResolvedValue({
      data: { user: { id: 'user-2', email: 'new@example.com' }, session: null },
      error: null,
    });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    await expect(auth.signUp('new@example.com', 'password')).resolves.toEqual({
      needsEmailConfirmation: true,
    });
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('signs up and authenticates when the session is immediately usable', async () => {
    const mock = createMockSupabase();
    const session = createTestSession();
    mock.auth.signUp.mockResolvedValue({
      data: { user: session.user, session },
      error: null,
    });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    await expect(auth.signUp('new@example.com', 'password')).resolves.toEqual({
      needsEmailConfirmation: false,
    });
  });

  it('normalizes duplicate-registration failures', async () => {
    const mock = createMockSupabase();
    mock.auth.signUp.mockResolvedValue({
      data: { user: null, session: null },
      error: { name: 'AuthApiError', message: 'User already registered', status: 400 },
    });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    await expect(auth.signUp('taken@example.com', 'password')).rejects.toThrow(
      'An account with this email already exists.',
    );
  });

  it('signs out and clears the authenticated state', async () => {
    const mock = createMockSupabase(createTestSession());
    mock.auth.signOut.mockResolvedValue({ error: null });
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();
    expect(auth.isAuthenticated()).toBe(true);

    await auth.signOut();

    expect(auth.isAuthenticated()).toBe(false);
    expect(auth.accessToken()).toBeNull();
  });

  it('keeps state in sync with auth state changes', async () => {
    const mock = createMockSupabase(null);
    TestBed.configureTestingModule({ providers: [{ provide: SUPABASE_CLIENT, useValue: mock.client }] });

    const auth = TestBed.inject(AuthService);
    await auth.initialize();
    expect(auth.isAuthenticated()).toBe(false);

    mock.emit('SIGNED_IN', createTestSession());
    expect(auth.isAuthenticated()).toBe(true);

    mock.emit('SIGNED_OUT', null);
    expect(auth.isAuthenticated()).toBe(false);
  });
});