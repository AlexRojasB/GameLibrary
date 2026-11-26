import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { authGuard, guestGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { SUPABASE_CLIENT } from './supabase-client';
import { createMockSupabase, createTestSession } from './testing/supabase-client.mock';

@Component({ template: '' })
class DummyComponent {}

describe('route guards', () => {
  it('shows recovery completion on Sign in without changing a normal authenticated session', async () => {
    const session = createTestSession();
    const mock = createMockSupabase(session);
    TestBed.configureTestingModule({ providers: [provideRouter([
      { path: '', component: DummyComponent },
      { path: 'login', component: DummyComponent, canActivate: [guestGuard] },
      { path: 'register', component: DummyComponent, canActivate: [guestGuard] },
    ]), { provide: SUPABASE_CLIENT, useValue: mock.client }] });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login', { info: 'passwordResetComplete' });
    expect(router.url).toBe('/login');
    expect(TestBed.inject(AuthService).session()).toBe(session);
    expect(mock.auth.signOut).not.toHaveBeenCalled();
    await router.navigateByUrl('/register', { info: 'passwordResetComplete' });
    expect(router.url).toBe('/'); // The presentation exception is not a general guard bypass.
  });

  it('ignores a legacy persisted completion flag for an authenticated user', async () => {
    const session = createTestSession();
    const mock = createMockSupabase(session);
    TestBed.configureTestingModule({ providers: [provideRouter([
      { path: '', component: DummyComponent, canActivate: [authGuard] },
      { path: 'login', component: DummyComponent, canActivate: [guestGuard] },
    ]), { provide: SUPABASE_CLIENT, useValue: mock.client }] });
    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login', { state: { passwordResetComplete: true } });
    expect(router.url).toBe('/');
    expect(TestBed.inject(AuthService).session()).toBe(session);
    expect(mock.auth.signOut).not.toHaveBeenCalled();
  });

  it('authGuard redirects an unauthenticated user to /login', async () => {
    const mock = createMockSupabase(null);
    TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [
        provideRouter([
          { path: '', component: DummyComponent, canActivate: [authGuard] },
          { path: 'login', component: DummyComponent },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');

    expect(router.url).toBe('/login');
  });

  it('authGuard permits an authenticated user', async () => {
    const mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [
        provideRouter([
          { path: '', component: DummyComponent, canActivate: [authGuard] },
          { path: 'login', component: DummyComponent },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');

    expect(router.url).toBe('/');
  });

  it('guestGuard permits an unauthenticated user on /login', async () => {
    const mock = createMockSupabase(null);
    TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [
        provideRouter([
          { path: '', component: DummyComponent },
          { path: 'login', component: DummyComponent, canActivate: [guestGuard] },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login');

    expect(router.url).toBe('/login');
  });

  it('guestGuard redirects an authenticated user away from /login', async () => {
    const mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [
        provideRouter([
          { path: '', component: DummyComponent },
          { path: 'login', component: DummyComponent, canActivate: [guestGuard] },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/login');

    expect(router.url).toBe('/');
  });

  it('authGuard fails closed and redirects to /login when restoration fails', async () => {
    const mock = createMockSupabase(null);
    mock.auth.getSession.mockRejectedValue(new Error('network down'));
    TestBed.configureTestingModule({
      imports: [DummyComponent],
      providers: [
        provideRouter([
          { path: '', component: DummyComponent, canActivate: [authGuard] },
          { path: 'login', component: DummyComponent },
        ]),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });

    const router = TestBed.inject(Router);
    await router.navigateByUrl('/');

    expect(router.url).toBe('/login');
  });
});
