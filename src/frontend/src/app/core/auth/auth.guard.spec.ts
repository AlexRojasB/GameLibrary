import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { authGuard, guestGuard } from './auth.guard';
import { SUPABASE_CLIENT } from './supabase-client';
import { createMockSupabase, createTestSession } from './testing/supabase-client.mock';

@Component({ template: '' })
class DummyComponent {}

describe('route guards', () => {
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