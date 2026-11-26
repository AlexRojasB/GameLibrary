import { expect, type BrowserContext, type Page } from '@playwright/test';

const userId = 'authenticated-navigation-user';
const accessToken = 'synthetic-authenticated-navigation-token';
export const email = 'synthetic@example.test';
export const password = 'synthetic-new-password';

export async function mockAuthenticatedNavigation(context: BrowserContext) {
  const fixture = { signIns: 0, authenticatedReads: 0 };
  await context.route('**/*', route => {
    expect(['127.0.0.1', 'localhost']).toContain(new URL(route.request().url()).hostname);
    return route.fallback();
  });
  await context.route('http://127.0.0.1:5218/**', async route => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    const json = (body: unknown, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    if (path === '/e2e/auth/session') {
      expect(request.method()).toBe('POST');
      expect(request.postDataJSON()).toEqual({ email, password });
      fixture.signIns++;
      return json({ accessToken, userId, email, expiresAt: Math.floor(Date.now() / 1000) + 3600 });
    }
    expect(request.method()).toBe('GET');
    if (path === '/health') return json({ status: 'ok' });
    if (request.headers()['authorization'] !== 'Bearer ' + accessToken) return json({}, 401);
    fixture.authenticatedReads++;
    if (path === '/auth/me') return json({ userId });
    if (path === '/currently-playing') return json({ videoGame: null, boardGame: null });
    if (['/library', '/platforms', '/genres', '/video-games', '/board-games', '/play-log'].includes(path)) return json([]);
    throw new Error(`Unexpected navigation fixture request: ${path}`);
  });
  return fixture;
}

export async function signIn(page: Page) {
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
}

export async function expectValidSession(page: Page) {
  // This verifies the mocked normal session and API boundary, not hosted JWT
  // cryptography. The real AuthService, restoration, guards and interceptor run.
  const evidence = await page.evaluate(async () => {
    const session = JSON.parse(localStorage.getItem('game-library-e2e-session') ?? 'null');
    const response = await fetch('http://127.0.0.1:5218/auth/me', {
      headers: session ? { Authorization: 'Bearer ' + session.access_token } : {},
    });
    return { status: response.status, userId: (await response.json()).userId,
      subject: session?.user.id, unexpired: session?.expires_at > Date.now() / 1000 };
  });
  expect(evidence).toEqual({ status: 200, userId, subject: userId, unexpired: true });
}

export async function primary(page: Page, name: string) {
  const nav = page.viewportSize()!.width < 760 ? '.app-shell__mobile-nav' : '.app-shell__desktop-nav';
  await page.locator(nav).getByRole('link', { name, exact: true }).click();
}

export async function watchLoginActivations(page: Page) {
  // Back may restore another document, not just trigger SPA popstate. Keep only
  // a test counter across those documents; never treat a lost observer as zero.
  await page.evaluate(() => sessionStorage.setItem('test-login-activations', '0'));
  const observe = () => {
    new MutationObserver(records => {
      for (const record of records) for (const node of record.addedNodes) {
        if (node instanceof Element && (node.matches('app-login') || node.querySelector('app-login'))) {
          sessionStorage.setItem('test-login-activations', String(Number(sessionStorage.getItem('test-login-activations')) + 1));
        }
      }
    }).observe(document, { childList: true, subtree: true });
  };
  await page.addInitScript(observe);
  await page.evaluate(observe);
}

export async function expectNoLoginActivation(page: Page) {
  expect(await page.evaluate(() => sessionStorage.getItem('test-login-activations'))).toBe('0');
}
