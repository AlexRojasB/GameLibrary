import { createHash } from 'node:crypto';
import { expect, test, type BrowserContext } from '@playwright/test';

import { expectValidSession, mockAuthenticatedNavigation, primary, signIn, watchLoginActivations, expectNoLoginActivation } from './fixtures/authenticated-navigation';

// Provider-boundary fixture: the application uses its real Supabase SDK, PKCE
// storage, exchange event, update and sign-out. No email or hosted call is made.
async function mockSupabase(context: BrowserContext) {
  const fixture = { challenge: '', recoverStatus: 200, expired: false, logoutFailures: 0, updates: 0, exchanges: 0, logouts: 0 };
  const user = { id: '00000000-0000-0000-0000-000000000008', aud: 'authenticated', role: 'authenticated',
    email: 'synthetic@example.test', app_metadata: { provider: 'email' }, user_metadata: {}, created_at: '2026-01-01T00:00:00Z' };
  const encode = (value: unknown) => Buffer.from(JSON.stringify(value)).toString('base64url');
  const access = `${encode({ alg: 'HS256', typ: 'JWT' })}.${encode({ sub: user.id, exp: Math.floor(Date.now() / 1000) + 3600 })}.synthetic`;
  await context.route('http://localhost:54321/auth/v1/**', async route => {
    const req = route.request();
    const url = new URL(req.url());
    expect(req.headers()['referer']).toBeUndefined(); // Callback/code must not become a request referrer.
    const json = async (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (url.pathname.endsWith('/recover')) {
      const body = req.postDataJSON();
      expect(url.searchParams.get('redirect_to')).toBe('http://127.0.0.1:4200/reset-password');
      expect(body.code_challenge_method).toBe('s256');
      fixture.challenge = body.code_challenge;
      return json(fixture.recoverStatus === 200 ? {} : { msg: 'Private provider details', code: fixture.recoverStatus === 429 ? 'over_email_send_rate_limit' : 'unexpected_failure' }, fixture.recoverStatus);
    }
    if (url.pathname.endsWith('/token')) {
      fixture.exchanges++;
      expect(url.searchParams.get('grant_type')).toBe('pkce');
      const body = req.postDataJSON();
      expect(body.auth_code).toBe('synthetic-recovery-code');
      expect(createHash('sha256').update(body.code_verifier).digest('base64url')).toBe(fixture.challenge);
      if (fixture.expired) return json({ code: 'otp_expired', msg: 'Expired synthetic code' }, 400);
      return json({ access_token: access, refresh_token: 'synthetic-refresh-token', token_type: 'bearer', expires_in: 3600, user });
    }
    if (url.pathname.endsWith('/user') && req.method() === 'PUT') {
      fixture.updates++;
      expect(req.postDataJSON()).toEqual({ password: 'synthetic-new-password', code_challenge: null, code_challenge_method: null });
      expect(req.headers()['authorization']).toBe('Bearer ' + access);
      return json(user);
    }
    if (url.pathname.endsWith('/logout')) {
      fixture.logouts++;
      expect(url.searchParams.get('scope')).toBe('local');
      if (fixture.logoutFailures-- > 0) return json({ code: 'unexpected_failure', msg: 'Private cleanup details' }, 503);
      return route.fulfill({ status: 204 });
    }
    if (url.pathname.endsWith('/user') && req.method() === 'GET') return json(user);
    throw new Error('Unexpected mocked Auth operation');
  });
  return fixture;
}

test('requests recovery with neutral confirmation, safe rate-limit feedback and no arbitrary redirect', async ({ page, context }) => {
  const fixture = await mockSupabase(context);
  await page.goto('/login');
  await page.getByRole('link', { name: 'Forgot password?', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Forgot password?', exact: true })).toBeVisible();
  fixture.recoverStatus = 429;
  await page.getByLabel('Email', { exact: true }).fill('synthetic@example.test');
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Too many requests');
  await expect(page.getByRole('alert')).not.toContainText('Private provider');
  fixture.recoverStatus = 200;
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  const message = 'If an account exists for that email address, you will receive a password reset email. Check your inbox and spam folder.';
  await expect(page.getByRole('status')).toHaveText(message);
  await page.goto('/forgot-password?redirectTo=https://evil.test');
  await page.getByLabel('Email', { exact: true }).fill('missing@example.test');
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText(message);
  expect(fixture.updates).toBe(0);
});

test('exchanges a recovery PKCE code, updates once, retries cleanup only, and rejects used/expired links', async ({ page, context }) => {
  const fixture = await mockSupabase(context);
  await page.goto('/forgot-password');
  await page.getByLabel('Email', { exact: true }).fill('synthetic@example.test');
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('If an account exists');
  // Existing normal session for another account. The harness uses its established
  // fake auth storage; recovery still goes through the real SDK PKCE boundary.
  const ordinarySession = await page.evaluate(() => {
    const value = JSON.stringify({ access_token: 'synthetic-ordinary-access-token', refresh_token: 'synthetic-ordinary-refresh-token',
      token_type: 'bearer', expires_in: 3600, expires_at: Math.floor(Date.now() / 1000) + 3600,
      user: { id: 'ordinary-unrelated-user', email: 'ordinary@example.test', app_metadata: {}, user_metadata: {}, aud: 'authenticated', created_at: '2026-01-01T00:00:00Z' } });
    localStorage.setItem('game-library-e2e-session', value);
    return value;
  });
  await page.goto('/reset-password?code=synthetic-recovery-code');
  const password = page.getByLabel('New password', { exact: true });
  await expect(password).toBeVisible();
  await expect(page).toHaveURL('http://127.0.0.1:4200/reset-password');
  await expect(password).toHaveAttribute('type', 'password');
  await expect(password).toHaveAttribute('autocomplete', 'new-password');
  await password.fill('short');
  await page.getByRole('button', { name: 'Update password', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('at least 6 characters');
  expect(fixture.updates).toBe(0);
  await password.fill('synthetic-new-password');
  await password.press('Tab');
  const show = page.getByRole('button', { name: 'Show password', exact: true });
  await expect(show).toBeFocused();
  await expect(show).toHaveAttribute('type', 'button');
  await expect(show).toHaveAttribute('aria-controls', 'reset-password');
  await expect(show).toHaveAccessibleName('Show password');
  await expect(show).toHaveText('');
  await expect(show.locator('svg')).toHaveAttribute('aria-hidden', 'true');
  await expect(show.locator('svg')).toHaveAttribute('focusable', 'false');
  await expect(show.locator('svg line')).toHaveCount(0);
  expect(await show.evaluate(button => button.matches(':focus-visible'))).toBe(true);
  expect(await show.evaluate(button => parseFloat(getComputedStyle(button).outlineWidth))).toBeGreaterThan(0);
  const box = await show.boundingBox();
  expect(box!.width).toBeGreaterThanOrEqual(44);
  expect(box!.height).toBeGreaterThanOrEqual(44);
  await show.press('Enter');
  await expect(password).toHaveAttribute('type', 'text');
  const hide = page.getByRole('button', { name: 'Hide password', exact: true });
  await expect(hide).toBeFocused();
  await expect(hide).toHaveAccessibleName('Hide password');
  await expect(hide.locator('svg line')).toHaveCount(1);
  await hide.press('Space');
  await expect(show).toBeFocused();
  await expect(password).toHaveAttribute('type', 'password');
  await expect(show.locator('svg line')).toHaveCount(0);
  await expect(password).toHaveValue('synthetic-new-password');
  expect(fixture.updates).toBe(0); // Neither keyboard activation submitted the valid form.
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  fixture.logoutFailures = 1;
  await page.getByRole('button', { name: 'Update password', exact: true }).click();
  await page.getByRole('button', { name: 'Retry sign-out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('Password updated. Sign in with your new password.');
  expect(await page.evaluate(() => localStorage.getItem('game-library-e2e-session'))).toBe(ordinarySession);
  expect(fixture.updates).toBe(1);
  // This SDK version clears its local session even when the logout POST fails.
  // Cleanup-only retry therefore confirms an empty SDK session without another
  // remote POST; it must not repeat the password update or invent credentials.
  expect(fixture.logouts).toBe(1);
  expect(await page.evaluate(() => localStorage.getItem('game-library-password-recovery'))).toBeNull();
  expect(await page.evaluate(() => localStorage.getItem('game-library-password-recovery-code-verifier'))).toBeNull();
  await page.goto('/reset-password?code=synthetic-recovery-code');
  await expect(page.getByRole('alert')).toContainText('invalid, expired or already used');
  await expect(password).toHaveCount(0);
  expect(await page.evaluate(() => localStorage.getItem('game-library-e2e-session'))).toBe(ordinarySession);
  await page.getByRole('link', { name: 'Request another reset email', exact: true }).click();
  await page.getByLabel('Email', { exact: true }).fill('synthetic@example.test');
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('If an account exists');
  fixture.expired = true;
  await page.goto('/reset-password?code=synthetic-recovery-code');
  await expect(page.getByRole('alert')).toContainText('invalid, expired or already used');
  expect(fixture.updates).toBe(1);
  expect(fixture.exchanges).toBe(2); // Replay failed locally without another exchange.
});

test('recovery then sign-in, Home and Back preserves the valid session without reopening Sign in', async ({ page, context }) => {
  const normal = await mockAuthenticatedNavigation(context);
  const recovery = await mockSupabase(context);
  await page.goto('/forgot-password');
  await page.getByLabel('Email', { exact: true }).fill('synthetic@example.test');
  await page.getByRole('button', { name: 'Send reset email', exact: true }).click();
  await expect(page.getByRole('status')).toContainText('If an account exists');
  await page.goto('/reset-password?code=synthetic-recovery-code');
  await page.getByLabel('New password', { exact: true }).fill('synthetic-new-password');
  await page.getByRole('button', { name: 'Update password', exact: true }).click();
  await expect(page.getByRole('status')).toHaveText('Password updated. Sign in with your new password.');
  const length = await page.evaluate(() => history.length);
  await signIn(page);
  await expectValidSession(page);
  const session = await page.evaluate(() => localStorage.getItem('game-library-e2e-session'));
  const operations = { exchanges: recovery.exchanges, updates: recovery.updates, logouts: recovery.logouts };
  await watchLoginActivations(page);
  await page.goBack();
  // Capture screen and session separately: a login screen is not proof of logout.
  const evidence = await page.evaluate(() => ({ pathname: location.pathname,
    authenticated: !!JSON.parse(localStorage.getItem('game-library-e2e-session') ?? 'null'),
    completionFlagInHistory: history.state?.passwordResetComplete === true }));
  await test.info().attach('back-evidence', { body: JSON.stringify(evidence), contentType: 'application/json' });
  expect(await page.evaluate(() => localStorage.getItem('game-library-e2e-session'))).toBe(session);
  await expectValidSession(page);
  await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toHaveCount(0);
  // The preceding email-request screen is public by GL-08; do not erase it or
  // invent a global history skip. The submitted Sign in entry was replaced.
  await expect(page.getByRole('heading', { name: 'Forgot password?', exact: true })).toBeVisible();
  await expectNoLoginActivation(page);
  expect(await page.evaluate(() => history.length)).toBe(length);
  await page.goForward();
  await expect(page).toHaveURL('/');
  await expectValidSession(page);
  await primary(page, 'Library'); // Real interceptor must also send the same token.
  await expect(page.getByRole('heading', { name: 'Browse the shelf.', exact: true })).toBeVisible();
  await expectNoLoginActivation(page);
  expect(normal.signIns).toBe(1);
  expect(normal.authenticatedReads).toBeGreaterThan(0);
  expect({ exchanges: recovery.exchanges, updates: recovery.updates, logouts: recovery.logouts }).toEqual(operations);
  expect(recovery.exchanges).toBe(1);
  expect(recovery.updates).toBe(1);
  expect(await page.evaluate(() => localStorage.getItem('game-library-password-recovery'))).toBeNull();
  expect(await page.evaluate(() => localStorage.getItem('game-library-password-recovery-code-verifier'))).toBeNull();
});
