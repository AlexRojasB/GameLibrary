import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroupDirective } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../core/auth/supabase-client';
import { PASSWORD_RECOVERY_CLIENT, RECOVERY_CODE } from '../../core/auth/password-recovery-client';
import { createMockSupabase, createTestSession } from '../../core/auth/testing/supabase-client.mock';
import { ForgotPassword } from './forgot-password/forgot-password';
import { ResetPassword } from './reset-password/reset-password';
import { Login } from './login/login';
import { Register } from './register/register';

@Component({ template: '' })
class LoginStub {}

function fill(fixture: ComponentFixture<unknown>, id: string, value: string): void {
  const input: HTMLInputElement = fixture.nativeElement.querySelector('#' + id);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('password reset email request', () => {
  let fixture: ComponentFixture<ForgotPassword>;
  let mock: ReturnType<typeof createMockSupabase>;
  beforeEach(() => {
    mock = createMockSupabase();
    mock.auth.resetPasswordForEmail.mockResolvedValue({ data: {}, error: null });
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: PASSWORD_RECOVERY_CLIENT, useValue: mock.client }] });
    fixture = TestBed.createComponent(ForgotPassword);
    fixture.detectChanges();
  });

  it('validates email before requesting and blocks duplicate submissions', async () => {
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.resetPasswordForEmail).not.toHaveBeenCalled();
    let resolve!: (value: unknown) => void;
    mock.auth.resetPasswordForEmail.mockReturnValue(new Promise(done => resolve = done));
    fill(fixture, 'recovery-email', 'synthetic@example.test');
    const pending = fixture.componentInstance.onSubmit();
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.resetPasswordForEmail).toHaveBeenCalledExactlyOnceWith('synthetic@example.test', { redirectTo: window.location.origin + '/reset-password' });
    resolve({ data: {}, error: null });
    await pending;
  });

  it.each(['known@example.test', 'missing@example.test', 'provider-4xx@example.test'])('uses the same neutral confirmation for %s', async email => {
    if (email.startsWith('provider')) mock.auth.resetPasswordForEmail.mockResolvedValue({ error: { status: 400, message: 'private account status' } });
    fill(fixture, 'recovery-email', email);
    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('If an account exists for that email address');
    expect(fixture.nativeElement.textContent).not.toContain(email);
    expect(fixture.nativeElement.textContent).not.toContain('private account status');
  });

  it.each([429, 503, 0])('handles failure %s with a safe retry message', async status => {
    mock.auth.resetPasswordForEmail.mockRejectedValue({ status, message: 'sensitive provider details' });
    fill(fixture, 'recovery-email', 'synthetic@example.test');
    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(status === 429 ? 'Too many requests' : 'Unable to request');
    expect(fixture.nativeElement.textContent).not.toContain('sensitive');
    mock.auth.resetPasswordForEmail.mockResolvedValue({ data: {}, error: null });
    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('If an account exists');
  });
});

describe('recovery-only password update', () => {
  afterEach(() => vi.restoreAllMocks());
  let fixture: ComponentFixture<ResetPassword>;
  let mock: ReturnType<typeof createMockSupabase>;
  let normalMock: ReturnType<typeof createMockSupabase>;
  let normalSession: ReturnType<typeof createTestSession>;
  let normalAuth: AuthService;
  let event: string;
  let session: ReturnType<typeof createTestSession>;
  async function setup(code: string | null = 'synthetic-code'): Promise<void> {
    TestBed.configureTestingModule({ providers: [
      provideRouter([{ path: 'login', component: LoginStub }]),
      { provide: PASSWORD_RECOVERY_CLIENT, useValue: mock.client },
      { provide: RECOVERY_CODE, useValue: () => code },
      { provide: SUPABASE_CLIENT, useValue: normalMock.client },
    ] });
    normalAuth = TestBed.inject(AuthService);
    await normalAuth.initialize();
    fixture = TestBed.createComponent(ResetPassword);
    await fixture.whenStable();
    fixture.detectChanges();
  }
  beforeEach(() => {
    mock = createMockSupabase();
    session = createTestSession();
    event = 'PASSWORD_RECOVERY';
    mock.auth.exchangeCodeForSession.mockImplementation(async () => {
      mock.emit(event, session);
      return { data: { session, user: session.user }, error: null };
    });
    mock.auth.getSession.mockImplementation(async () => ({ data: { session }, error: null }));
    mock.auth.updateUser.mockResolvedValue({ data: { user: session.user }, error: null });
    mock.auth.signOut.mockResolvedValue({ error: null });
    normalSession = createTestSession({ user: { ...session.user, id: 'ordinary-user' }, access_token: 'ordinary-access-token' });
    normalMock = createMockSupabase(normalSession);
  });

  it('requires a fresh matching recovery exchange, preserves GL-07 toggling, and validates the new password', async () => {
    await setup();
    const password: HTMLInputElement = fixture.nativeElement.querySelector('#reset-password');
    expect(password.type).toBe('password');
    expect(password.autocomplete).toBe('new-password');
    expect(password.labels?.[0].textContent).toBe('New password');
    fill(fixture, 'reset-password', 'short');
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
    const toggle: HTMLButtonElement = fixture.nativeElement.querySelector('button[aria-controls]');
    expect(toggle.type).toBe('button');
    expect(toggle.getAttribute('aria-label')).toBe('Show password');
    expect(toggle.getAttribute('aria-controls')).toBe(password.id);
    expect(toggle.textContent.trim()).toBe('');
    expect(toggle.querySelector('svg')?.getAttribute('aria-hidden')).toBe('true');
    expect(toggle.querySelector('svg')?.getAttribute('focusable')).toBe('false');
    expect(toggle.querySelector('svg line')).toBeNull();
    for (const [type, name] of [['text', 'Hide password'], ['password', 'Show password']]) {
      toggle.click(); fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('#reset-password')).toBe(password);
      expect(password.type).toBe(type);
      expect(password.value).toBe('short');
      expect(toggle.getAttribute('aria-label')).toBe(name);
      expect(toggle.textContent.trim()).toBe('');
      expect(toggle.querySelectorAll('svg line')).toHaveLength(type === 'text' ? 1 : 0);
    }
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
    fill(fixture, 'reset-password', 'synthetic-new-password');
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.updateUser).toHaveBeenCalledExactlyOnceWith({ password: 'synthetic-new-password' });
    expect(mock.auth.signOut).toHaveBeenCalledWith({ scope: 'local' });
    expect(normalMock.auth.signOut).not.toHaveBeenCalled();
    expect(normalAuth.session()).toBe(normalSession);
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.info).toBe('passwordResetComplete');
    expect(TestBed.inject(Router).lastSuccessfulNavigation()?.extras.state).toBeUndefined();
  });

  it('toggles the valid recovery field through its icon without submitting or changing validation/focus', async () => {
    await setup();
    fill(fixture, 'reset-password', 'synthetic-new-password');
    const password: HTMLInputElement = fixture.nativeElement.querySelector('#reset-password');
    const button: HTMLButtonElement = fixture.nativeElement.querySelector('button[aria-controls]');
    const control = fixture.debugElement.query(By.directive(FormGroupDirective)).injector.get(FormGroupDirective).control.get('password')!;
    const before = { value: control.value, errors: control.errors, status: control.status, dirty: control.dirty, touched: control.touched };
    const submit = vi.spyOn(fixture.componentInstance, 'onSubmit');
    button.focus();
    for (const shown of [true, false]) {
      button.querySelector('svg')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      fixture.detectChanges();
      expect(document.activeElement).toBe(button);
      expect(fixture.nativeElement.querySelector('#reset-password')).toBe(password);
      expect(password.type).toBe(shown ? 'text' : 'password');
      expect(password.autocomplete).toBe('new-password');
      expect(password.required).toBe(true);
      expect(button.getAttribute('aria-label')).toBe(shown ? 'Hide password' : 'Show password');
      expect(button.querySelectorAll('svg line')).toHaveLength(shown ? 1 : 0);
      expect({ value: control.value, errors: control.errors, status: control.status, dirty: control.dirty, touched: control.touched }).toEqual(before);
    }
    expect(submit).not.toHaveBeenCalled();
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
    expect(normalMock.auth.signInWithPassword).not.toHaveBeenCalled();
    (fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(submit).toHaveBeenCalledTimes(1);
    expect(mock.auth.updateUser).toHaveBeenCalledExactlyOnceWith({ password: 'synthetic-new-password' });
  });

  it('keeps recovery visibility independent from both ordinary password fields', async () => {
    await setup();
    const login = TestBed.createComponent(Login);
    const register = TestBed.createComponent(Register);
    login.detectChanges(); register.detectChanges();
    const recoveryToggle: HTMLButtonElement = fixture.nativeElement.querySelector('button[aria-controls]');
    recoveryToggle.click(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#reset-password').type).toBe('text');
    expect(login.nativeElement.querySelector('#login-password').type).toBe('password');
    expect(register.nativeElement.querySelector('#register-password').type).toBe('password');
    (login.nativeElement.querySelector('button[aria-controls]') as HTMLButtonElement).click();
    recoveryToggle.click(); login.detectChanges(); fixture.detectChanges();
    expect(login.nativeElement.querySelector('#login-password').type).toBe('text');
    expect(fixture.nativeElement.querySelector('#reset-password').type).toBe('password');
    expect(register.nativeElement.querySelector('#register-password').type).toBe('password');
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
  });

  it.each(['SIGNED_IN', 'INITIAL_SESSION', 'TOKEN_REFRESHED'])('rejects ordinary/restored %s context even with a valid session', async ordinary => {
    event = ordinary;
    await setup();
    expect(fixture.nativeElement.querySelector('#reset-password')).toBeNull();
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('invalid, expired or already used');
  });

  it('rejects a recovery event for a different returned session', async () => {
    mock.auth.exchangeCodeForSession.mockImplementation(async () => {
      mock.emit('PASSWORD_RECOVERY', createTestSession({ access_token: 'other-synthetic-token' }));
      return { data: { session }, error: null };
    });
    await setup();
    expect(fixture.nativeElement.querySelector('#reset-password')).toBeNull();
  });

  it.each(['missing', 'expired', 'used', 'network'])('rejects %s links and offers another email without raw errors', async reason => {
    if (reason === 'expired') session.expires_at = 1;
    if (reason === 'used' || reason === 'network') mock.auth.exchangeCodeForSession.mockResolvedValue({ data: { session: null }, error: { message: 'private token value', status: reason === 'used' ? 400 : 0 } });
    await setup(reason === 'missing' ? null : 'synthetic-code');
    expect(fixture.nativeElement.querySelector('#reset-password')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/forgot-password"]')).toBeTruthy();
    expect(fixture.nativeElement.textContent).not.toContain('private token');
    expect(normalMock.auth.signOut).not.toHaveBeenCalled(); // Invalid recovery never signs ordinary users out.
    expect(normalAuth.session()).toBe(normalSession);
  });

  it.each(['identity', 'expiry', 'signout'])('rechecks %s before sending a new password', async reason => {
    await setup();
    fill(fixture, 'reset-password', 'synthetic-new-password');
    if (reason === 'identity') mock.auth.getSession.mockResolvedValue({ data: { session: createTestSession({ user: { ...session.user, id: 'other' } }) }, error: null });
    if (reason === 'expiry') vi.spyOn(Date, 'now').mockReturnValue(session.expires_at! * 1000 + 1);
    if (reason === 'signout') mock.emit('SIGNED_OUT', null);
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
    vi.restoreAllMocks();
  });

  it('retries only recovery cleanup after an acknowledged update, preserving the normal session', async () => {
    await setup();
    mock.auth.signOut.mockResolvedValueOnce({ error: { status: 503 } });
    fill(fixture, 'reset-password', 'synthetic-new-password');
    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#reset-password')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Retry sign-out');
    expect(fixture.nativeElement.textContent).toContain('recovery session');
    expect(fixture.nativeElement.textContent).not.toContain('private cleanup');
    await fixture.componentInstance.retryCleanup();
    expect(mock.auth.updateUser).toHaveBeenCalledTimes(1);
    expect(normalMock.auth.signOut).not.toHaveBeenCalled();
    expect(normalAuth.session()).toBe(normalSession);
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it.each(['same-account', 'different-account', 'changed-during-cleanup'])('does not disrupt a %s normal session on success', async normal => {
    if (normal === 'same-account') {
      normalSession = createTestSession({ access_token: 'ordinary-session-for-same-account' });
      normalMock.auth.getSession.mockResolvedValue({ data: { session: normalSession }, error: null });
    }
    await setup();
    if (normal === 'changed-during-cleanup') mock.auth.signOut.mockImplementation(async () => {
      normalSession = createTestSession({ user: { ...session.user, id: 'new-ordinary-user' }, access_token: 'new-ordinary-token' });
      normalMock.emit('SIGNED_IN', normalSession);
      return { error: null };
    });
    fill(fixture, 'reset-password', 'synthetic-new-password');
    await fixture.componentInstance.onSubmit();
    expect(normalMock.auth.signOut).not.toHaveBeenCalled();
    expect(normalMock.auth.signInWithPassword).not.toHaveBeenCalled();
    expect(normalAuth.session()).toBe(normalSession);
    expect(normalAuth.isAuthenticated()).toBe(true);
    expect(mock.auth.signOut).toHaveBeenCalledWith({ scope: 'local' });
    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('does not repeat an uncertain/failed update or show raw provider errors', async () => {
    await setup();
    mock.auth.updateUser.mockRejectedValue(new Error('private request/password details'));
    fill(fixture, 'reset-password', 'synthetic-new-password');
    await fixture.componentInstance.onSubmit();
    await fixture.componentInstance.onSubmit();
    fixture.detectChanges();
    expect(mock.auth.updateUser).toHaveBeenCalledTimes(1);
    expect(fixture.nativeElement.textContent).toContain('Unable to confirm');
    expect(fixture.nativeElement.textContent).not.toContain('private request');
  });

  it('blocks duplicate updates and does not navigate or end the normal session after destruction', async () => {
    await setup();
    let resolve!: (value: unknown) => void;
    mock.auth.updateUser.mockReturnValue(new Promise(done => resolve = done));
    fill(fixture, 'reset-password', 'synthetic-new-password');
    const pending = fixture.componentInstance.onSubmit();
    await Promise.resolve();
    await fixture.componentInstance.onSubmit();
    expect(mock.auth.updateUser).toHaveBeenCalledTimes(1);
    fixture.destroy();
    resolve({ data: { user: session.user }, error: null });
    await pending;
    expect(normalMock.auth.signOut).not.toHaveBeenCalled();
    expect(normalAuth.session()).toBe(normalSession);
    expect(TestBed.inject(Router).url).not.toBe('/login');
  });

  it('cleans a delayed verified session without enabling a destroyed screen', async () => {
    let resolve!: (value: unknown) => void;
    mock.auth.exchangeCodeForSession.mockReturnValue(new Promise(done => resolve = done));
    // Cannot await whenStable while the exchange is deliberately unresolved.
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: PASSWORD_RECOVERY_CLIENT, useValue: mock.client },
      { provide: RECOVERY_CODE, useValue: () => 'synthetic-code' },
      { provide: SUPABASE_CLIENT, useValue: normalMock.client },
    ] });
    fixture = TestBed.createComponent(ResetPassword);
    mock.emit('PASSWORD_RECOVERY', session);
    fixture.destroy();
    resolve({ data: { session }, error: null });
    await Promise.resolve();
    await Promise.resolve();
    expect(mock.auth.signOut).toHaveBeenCalledWith({ scope: 'local' });
    expect(normalMock.auth.signOut).not.toHaveBeenCalled();
    expect(mock.auth.updateUser).not.toHaveBeenCalled();
  });
});
