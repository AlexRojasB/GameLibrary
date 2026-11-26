import { Location, PlatformLocation } from '@angular/common';
import { MockPlatformLocation } from '@angular/common/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { CanActivateFn, NavigationCancel, NavigationEnd, NavigationSkipped, provideRouter, Router, RouterOutlet, withRouterConfig } from '@angular/router';
import { filter, firstValueFrom, take } from 'rxjs';

import { authGuard } from '../auth/auth.guard';
import { AuthService } from '../auth/auth.service';
import { SUPABASE_CLIENT } from '../auth/supabase-client';
import { createMockSupabase, createTestSession } from '../auth/testing/supabase-client.mock';
import { BackButton } from '../../shared/back-button/back-button';
import { BackNavigation } from './back-navigation';

@Component({ template: '<app-back-button fallback="/manage" />', imports: [BackButton] })
class PageStub {}

@Component({ template: '<router-outlet />', imports: [RouterOutlet] })
class Host {}

describe('BackNavigation with the real Angular router and browser-entry model', () => {
  let router: Router;
  let location: Location;
  let platform: MockPlatformLocation;
  let back: BackNavigation;
  let mock: ReturnType<typeof createMockSupabase>;
  let allow: boolean;
  let fail: boolean;

  beforeEach(async () => {
    mock = createMockSupabase(createTestSession());
    allow = true;
    fail = false;
    const testGuard: CanActivateFn = () => {
      if (fail) throw new Error('Synthetic route failure');
      return allow;
    };
    TestBed.configureTestingModule({ imports: [Host], providers: [
      provideRouter([
        ...['', 'library', 'manage', 'platforms', 'video-games', 'board-games', 'play-log', 'random-picker']
          .map(path => ({ path, component: PageStub, canActivate: [authGuard] })),
        { path: 'health', component: PageStub, canActivate: [testGuard] },
        ...['login', 'register', 'forgot-password', 'reset-password', 'manage/steam']
          .map(path => ({ path, component: PageStub })),
      ], withRouterConfig({ onSameUrlNavigation: 'reload' })),
      { provide: PlatformLocation, useClass: MockPlatformLocation },
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ] });
    router = TestBed.inject(Router);
    router.setUpLocationChangeListener();
    location = TestBed.inject(Location);
    platform = TestBed.inject(PlatformLocation) as unknown as MockPlatformLocation;
    back = TestBed.inject(BackNavigation); // Observe before the first navigation.
    await TestBed.inject(AuthService).initialize();
    TestBed.createComponent(Host).detectChanges();
  });

  async function traverse(action: () => void): Promise<void> {
    const done = firstValueFrom(router.events.pipe(filter(event => event instanceof NavigationEnd), take(1)));
    action();
    await done;
    // Router clears currentNavigation after delivering NavigationEnd.
    await Promise.resolve();
  }

  it('returns to the previous meaningful screen without appending, retaining browser Forward and Back', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    expect(back.label('/manage')).toBe('Back to Library');
    const push = vi.spyOn(platform, 'pushState');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/library');
    expect(push).not.toHaveBeenCalled();
    await traverse(() => location.forward());
    expect(router.url).toBe('/platforms');
    expect(back.label('/manage')).toBe('Back to Library');
    await traverse(() => location.back());
    expect(router.url).toBe('/library');
  });

  it('uses replaceUrl parent fallback on direct entry even with external/old history and caller state', async () => {
    location.go('/outside', '', { navigationId: 400, returnTo: 'https://evil.example' });
    await router.navigateByUrl('/platforms', { state: { returnTo: 'https://evil.example', passwordResetComplete: true } });
    expect(back.label('/manage')).toBe('Back to Manage');
    const push = vi.spyOn(platform, 'pushState');
    const replace = vi.spyOn(platform, 'replaceState');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/manage');
    expect(push).not.toHaveBeenCalled();
    expect(replace).toHaveBeenCalled();
    expect(location.getState()).not.toHaveProperty('returnTo');
  });

  it('does not reuse pre-reload history when initialized after an existing navigation', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/video-games');
    // A fresh instance has no evidence of the browser's older entries.
    const fresh = TestBed.runInInjectionContext(() => new BackNavigation());
    expect(fresh.label('/manage')).toBe('Back to Manage');
    await traverse(() => fresh.back('/manage'));
    expect(router.url).toBe('/manage');
  });

  it('uses guarded Home as the health/mobile Play Log direct-entry fallback', async () => {
    await router.navigateByUrl('/health');
    expect(back.label('/')).toBe('Back to Home');
    await traverse(() => back.back('/'));
    expect(router.url).toBe('/');
  });

  it('accounts for replacement and same-URL reload without phantom entries or loops', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/video-games');
    await router.navigateByUrl('/board-games', { replaceUrl: true });
    await router.navigateByUrl('/board-games');
    expect(back.label('/manage')).toBe('Back to Library');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/library');
    await traverse(() => location.forward());
    expect(router.url).toBe('/board-games');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/library');
  });

  it('tracks browser Back then a new branch rather than a chronological visit stack', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    await router.navigateByUrl('/video-games');
    await traverse(() => location.back());
    await router.navigateByUrl('/board-games');
    expect(back.label('/manage')).toBe('Back to Platforms');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/platforms');
    await traverse(() => location.forward());
    expect(router.url).toBe('/board-games');
  });

  it.each([
    '/login', '/register', '/forgot-password', '/reset-password?code=synthetic',
    '/%72eset-password', '/reset-password;callback=synthetic',
    '/manage/steam', '/manage/steam?steamLink=review', '/library?access_token=synthetic',
    '/library#access_token=synthetic', '/library;code=synthetic',
  ])('never traverses back to or across the excluded route %s', async excluded => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl(excluded);
    await router.navigateByUrl('/platforms');
    expect(back.label('/manage')).toBe('Back to Manage');
    const go = vi.spyOn(location, 'historyGo');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/manage');
    expect(go).not.toHaveBeenCalled();
  });

  it('discards unknown browser traversal IDs rather than trusting arbitrary state', async () => {
    await router.navigateByUrl('/library');
    location.go('/health', '', { navigationId: 999 });
    await router.navigateByUrl('/platforms');
    await traverse(() => location.back());
    expect(router.url).toBe('/health');
    expect(back.label('/')).toBe('Back to Home');
    await traverse(() => back.back('/'));
    expect(router.url).toBe('/');
  });

  it('discards unknown traversal without a navigation ID too', async () => {
    await router.navigateByUrl('/library');
    location.go('/health');
    await router.navigateByUrl('/platforms');
    await traverse(() => location.back());
    expect(back.label('/')).toBe('Back to Home');
  });

  it('does not traverse over an entry inserted outside the router', async () => {
    await router.navigateByUrl('/library');
    location.go('/outside?code=synthetic');
    await router.navigateByUrl('/platforms');
    expect(back.label('/manage')).toBe('Back to Manage');
    const go = vi.spyOn(location, 'historyGo');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/manage');
    expect(go).not.toHaveBeenCalled();
  });

  it('does not infer browser entries from skipLocationChange', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/video-games', { skipLocationChange: true });
    await router.navigateByUrl('/platforms');
    expect(back.label('/manage')).toBe('Back to Manage');
  });

  it('keeps the cursor usable after a same-URL NavigationSkipped event', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    expect(await router.navigateByUrl('/platforms', { onSameUrlNavigation: 'ignore' })).toBe(false);
    expect(back.busy()).toBe(false);
    expect(back.label('/manage')).toBe('Back to Library');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/library');
  });

  it('refuses a stale/changed current history entry and sanitizes even an invalid runtime fallback', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/health');
    location.replaceState('/health', '', { navigationId: 999 });
    const go = vi.spyOn(location, 'historyGo');
    // TypeScript callers cannot supply this; runtime safety still fixes it to Home.
    await traverse(() => back.back('https://evil.example' as '/'));
    expect(router.url).toBe('/');
    expect(go).not.toHaveBeenCalled();
  });

  it('leaves the known chain intact after cancelled/failed navigations', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    allow = false;
    expect(await router.navigateByUrl('/health')).toBe(false);
    expect(back.busy()).toBe(false);
    fail = true;
    await expect(router.navigateByUrl('/health')).rejects.toThrow('Synthetic route failure');
    expect(back.busy()).toBe(false);
    expect(back.label('/manage')).toBe('Back to Library');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/library');
  });

  it('invalidates offsets after a browser traversal is cancelled', async () => {
    location.go('/outside'); // Outside the ledger; must never be chosen by in-app Back.
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/health');
    await router.navigateByUrl('/platforms');
    allow = false;
    const done = firstValueFrom(router.events.pipe(filter(event => event instanceof NavigationCancel), take(1)));
    back.back('/manage');
    await done;
    await Promise.resolve();
    expect(router.url).toBe('/platforms');
    expect(back.busy()).toBe(false);
    expect(back.label('/manage')).toBe('Back to Manage');
    const go = vi.spyOn(location, 'historyGo');
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/manage');
    expect(go).not.toHaveBeenCalled();
  });

  it('invalidates offsets after a same-URL browser traversal is skipped', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    // An unknown duplicate browser slot, not a router-created same-URL reload.
    location.go('/platforms');
    const done = firstValueFrom(router.events.pipe(filter(event => event instanceof NavigationSkipped), take(1)));
    router.onSameUrlNavigation = 'ignore';
    location.back();
    await done;
    expect(back.label('/manage')).toBe('Back to Manage');
    expect(back.busy()).toBe(false);
  });

  it('resets on logout/account switch but not token refresh', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    mock.emit('TOKEN_REFRESHED', createTestSession({ access_token: 'fresh-synthetic' }));
    expect(back.label('/manage')).toBe('Back to Library');
    const user = createTestSession().user;
    mock.emit('SIGNED_IN', createTestSession({ user: { ...user, id: 'other-user' } }));
    expect(back.label('/manage')).toBe('Back to Manage');
    mock.emit('SIGNED_OUT', null);
    await traverse(() => back.back('/manage'));
    expect(router.url).toBe('/login');
  });

  it('runs authGuard again on a known history target, not only on fallback', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/health');
    const auth = TestBed.inject(AuthService);
    // Simulate expiry at authorization time, before a session notification arrives.
    vi.spyOn(auth, 'isAuthenticated').mockReturnValue(false);
    await traverse(() => back.back('/'));
    expect(router.url).toBe('/login');
    expect(back.busy()).toBe(false);
  });

  it('blocks duplicate Back activation while a traversal is pending', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    const go = vi.spyOn(location, 'historyGo');
    await traverse(() => { back.back('/manage'); back.back('/manage'); });
    expect(go).toHaveBeenCalledExactlyOnceWith(-1);
    expect(back.busy()).toBe(false);
  });

  it('renders one native non-submit button with a descriptive label and disables it during navigation', async () => {
    await router.navigateByUrl('/library');
    await router.navigateByUrl('/platforms');
    const fixture = TestBed.createComponent(BackButton);
    fixture.componentRef.setInput('fallback', '/manage');
    fixture.detectChanges();
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;
    expect(button.type).toBe('button');
    expect(button.textContent).toContain('Back to Library');
    expect(fixture.nativeElement.querySelector('a')).toBeNull();
    const go = vi.spyOn(location, 'historyGo').mockImplementation(() => { /* Leave traversal pending. */ });
    button.click();
    fixture.detectChanges();
    expect(button.disabled).toBe(true);
    button.click();
    expect(go).toHaveBeenCalledTimes(1);
  });
});
