import { Location, PlatformLocation } from '@angular/common';
import { MockPlatformLocation } from '@angular/common/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { NavigationEnd, provideRouter, Router, RouterOutlet } from '@angular/router';
import { filter, firstValueFrom, take } from 'rxjs';

import { routes } from '../../app.routes';
import { Login } from '../../features/auth/login/login';
import { ResetPassword } from '../../features/auth/reset-password/reset-password';
import { AuthService } from './auth.service';
import { PASSWORD_RECOVERY_CLIENT, RECOVERY_CODE } from './password-recovery-client';
import { SUPABASE_CLIENT } from './supabase-client';
import { createMockSupabase, createTestSession } from './testing/supabase-client.mock';

@Component({ template: '<h1>Protected screen</h1>' })
class PageStub {}

@Component({ template: '<router-outlet />', imports: [RouterOutlet] })
class Host {}

describe('authenticated navigation with the real router, guards and auth components', () => {
  let fixture: ComponentFixture<Host>;
  let router: Router;
  let location: Location;
  let platform: MockPlatformLocation;
  let normal: ReturnType<typeof createMockSupabase>;
  let recovery: ReturnType<typeof createMockSupabase>;
  const session = createTestSession();

  function setup(initialSession: ReturnType<typeof createTestSession> | null = null,
    restoration?: Promise<unknown>): void {
    normal = createMockSupabase(initialSession);
    if (restoration) normal.auth.getSession.mockReturnValue(restoration);
    normal.auth.signInWithPassword.mockResolvedValue({ data: { session }, error: null });
    normal.auth.signOut.mockResolvedValue({ error: null });
    recovery = createMockSupabase();
    recovery.auth.exchangeCodeForSession.mockImplementation(async () => {
      recovery.emit('PASSWORD_RECOVERY', session);
      return { data: { session }, error: null };
    });
    recovery.auth.getSession.mockResolvedValue({ data: { session }, error: null });
    recovery.auth.updateUser.mockResolvedValue({ data: { user: session.user }, error: null });
    recovery.auth.signOut.mockResolvedValue({ error: null });
    let code: string | null = 'synthetic-recovery-code';
    TestBed.configureTestingModule({ providers: [
      provideRouter(routes.filter(route => ['', 'login', 'register', 'reset-password', 'health', 'library', 'manage'].includes(route.path!))
        .map(route => ({ ...route, component: route.component === Login || route.component === ResetPassword ? route.component : PageStub }))),
      { provide: PlatformLocation, useClass: MockPlatformLocation },
      { provide: SUPABASE_CLIENT, useValue: normal.client },
      { provide: PASSWORD_RECOVERY_CLIENT, useValue: recovery.client },
      { provide: RECOVERY_CODE, useValue: () => { const value = code; code = null; return value; } },
    ] });
    router = TestBed.inject(Router);
    router.setUpLocationChangeListener();
    location = TestBed.inject(Location);
    platform = TestBed.inject(PlatformLocation) as unknown as MockPlatformLocation;
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
  }

  async function navigate(url: string): Promise<void> {
    await router.navigateByUrl(url);
    await fixture.whenStable();
  }

  async function traverse(action: () => void): Promise<void> {
    const done = firstValueFrom(router.events.pipe(filter(event => event instanceof NavigationEnd), take(1)));
    action();
    await done;
    await fixture.whenStable();
  }

  function fill(id: string, value: string): void {
    const input: HTMLInputElement = fixture.nativeElement.querySelector('#' + id);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  async function signIn(): Promise<void> {
    fill('login-email', 'user@example.com');
    fill('login-password', 'synthetic-new-password');
    await fixture.debugElement.query(By.directive(Login)).componentInstance.onSubmit();
    await fixture.whenStable();
    expect(router.url).toBe('/');
    expect(TestBed.inject(AuthService).session()).toBe(session);
  }

  function expectValidSession(): void {
    const auth = TestBed.inject(AuthService);
    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.session()).toBe(session);
    expect(auth.accessToken()).toBe(session.access_token);
    expect(normal.auth.signOut).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('app-login')).toBeNull();
  }

  it('completes recovery, signs in, and returns through Back/Forward without replay or session loss', async () => {
    setup();
    await navigate('/health');
    await navigate('/reset-password');
    fill('reset-password', 'synthetic-new-password');
    await fixture.debugElement.query(By.directive(ResetPassword)).componentInstance.onSubmit();
    await fixture.whenStable();
    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.textContent).toContain('Password updated. Sign in with your new password.');
    expect(location.getState()).not.toHaveProperty('passwordResetComplete');
    const cleanupCount = recovery.auth.signOut.mock.calls.length;
    const push = vi.spyOn(platform, 'pushState');
    await signIn();
    expect(push).not.toHaveBeenCalled();
    await traverse(() => location.back());
    expect(router.url).toBe('/health');
    expectValidSession();
    await traverse(() => location.forward());
    expect(router.url).toBe('/');
    expectValidSession();
    expect(normal.auth.signInWithPassword).toHaveBeenCalledTimes(1);
    expect(recovery.auth.exchangeCodeForSession).toHaveBeenCalledTimes(1);
    expect(recovery.auth.updateUser).toHaveBeenCalledTimes(1);
    expect(recovery.auth.signOut).toHaveBeenCalledTimes(cleanupCount);
  });

  it('replaces ordinary Sign in and preserves legitimate Back/Forward without duplicate entries', async () => {
    setup();
    await navigate('/health');
    await navigate('/login');
    const push = vi.spyOn(platform, 'pushState');
    await signIn();
    expect(push).not.toHaveBeenCalled();
    for (let i = 0; i < 3; i++) {
      await traverse(() => location.back());
      expect(router.url).toBe('/health');
      await traverse(() => location.forward());
      expect(router.url).toBe('/');
      expectValidSession();
    }
    expect(push).not.toHaveBeenCalled();
    await navigate('/library');
    await navigate('/manage');
    await traverse(() => location.back());
    expect(router.url).toBe('/library');
    await traverse(() => location.forward());
    expect(router.url).toBe('/manage');
    expectValidSession();
  });

  it('rejects a legacy completion history entry without activating Login or truncating Forward', async () => {
    setup(session);
    await navigate('/health');
    location.go('/login', '', { navigationId: 100, passwordResetComplete: true });
    await navigate('/library');
    const push = vi.spyOn(platform, 'pushState');
    for (let i = 0; i < 3; i++) {
      await traverse(() => location.back());
      expect(router.url).toBe('/');
      expectValidSession();
      await traverse(() => location.forward());
      expect(router.url).toBe('/library');
    }
    expect(push).not.toHaveBeenCalled();
    expect(recovery.auth.exchangeCodeForSession).not.toHaveBeenCalled();
  });

  it('awaits delayed restoration before deciding a direct login link, without a login flash', async () => {
    let restore!: (value: unknown) => void;
    setup(null, new Promise(resolve => restore = resolve));
    const pending = router.navigateByUrl('/login');
    await Promise.resolve();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-login')).toBeNull();
    restore({ data: { session }, error: null });
    await pending;
    await fixture.whenStable();
    expect(router.url).toBe('/');
    expectValidSession();
    expect(normal.auth.getSession).toHaveBeenCalledTimes(1);
    await navigate('/library');
    expect(router.url).toBe('/library');
    expectValidSession();
  });

  it('still permits immediate recovery confirmation with an existing normal session only for that visit', async () => {
    setup(session);
    await navigate('/health');
    await router.navigateByUrl('/login', { replaceUrl: true, info: 'passwordResetComplete' });
    await fixture.whenStable();
    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.textContent).toContain('Password updated. Sign in with your new password.');
    expect(TestBed.inject(AuthService).session()).toBe(session);
    expect(location.getState()).not.toHaveProperty('passwordResetComplete');
    await navigate('/library');
    await traverse(() => location.back());
    expect(router.url).toBe('/');
    expectValidSession();
  });

  it('keeps signed-out routes guarded and leaves a failed login on the form', async () => {
    setup();
    await navigate('/library');
    expect(router.url).toBe('/login');
    normal.auth.signInWithPassword.mockResolvedValueOnce({ data: { session: null }, error: { message: 'Invalid login credentials' } });
    fill('login-email', 'user@example.com');
    fill('login-password', 'incorrect');
    await fixture.debugElement.query(By.directive(Login)).componentInstance.onSubmit();
    await fixture.whenStable();
    expect(router.url).toBe('/login');
    expect(fixture.nativeElement.textContent).toContain('Invalid email or password.');
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    await navigate('/');
    expect(router.url).toBe('/login');
  });
});
