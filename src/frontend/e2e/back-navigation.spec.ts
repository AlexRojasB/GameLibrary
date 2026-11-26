import { expect, test, type Page } from '@playwright/test';

// Navigation-only browser coverage. Existing E2E Auth storage is seeded and API
// reads are mocked: no database, mutation, real account, Steam or hosted Auth call.
async function prepare(page: Page) {
  await page.route('http://127.0.0.1:5218/**', async route => {
    const path = new URL(route.request().url()).pathname;
    expect(route.request().method()).toBe('GET');
    let body: unknown;
    if (path === '/auth/me') body = { userId: 'navigation-test-user' };
    else if (path === '/currently-playing') body = { videoGame: null, boardGame: null };
    else if (path === '/health') body = { status: 'ok' };
    else if (['/library', '/platforms', '/genres', '/video-games', '/board-games', '/play-log'].includes(path)) body = [];
    else throw new Error(`Unexpected navigation fixture request: ${path}`);
    await route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
  });
  await page.goto('/login');
  await page.evaluate(() => localStorage.setItem('game-library-e2e-session', JSON.stringify({
    access_token: 'synthetic-navigation-token', refresh_token: 'synthetic-navigation-refresh',
    token_type: 'bearer', expires_in: 3600, expires_at: Math.floor(Date.now() / 1000) + 3600,
    user: { id: 'navigation-test-user', email: 'navigation@example.test', aud: 'authenticated',
      app_metadata: {}, user_metadata: {}, created_at: '2026-01-01T00:00:00Z' },
  })));
}

async function primary(page: Page, name: string) {
  const nav = page.viewportSize()!.width < 760 ? '.app-shell__mobile-nav' : '.app-shell__desktop-nav';
  await page.locator(nav).getByRole('link', { name, exact: true }).click();
}

test.beforeEach(async ({ page }) => prepare(page));

test('returns from a catalog to Library, retains Forward, and handles a new history branch', async ({ page }) => {
  await page.goto('/library');
  await expect(page.locator('app-back-button')).toHaveCount(0);
  await page.getByRole('link', { name: 'Add video games', exact: true }).click();
  const back = page.getByRole('button', { name: 'Back to Library', exact: true });
  await expect(back).toBeVisible();
  await expect(page.locator('app-back-button')).toHaveCount(1);
  const length = await page.evaluate(() => history.length);
  const box = await back.boundingBox();
  expect(box!.height).toBeGreaterThanOrEqual(44);
  expect(box!.x).toBeGreaterThanOrEqual(0);
  expect(box!.x + box!.width).toBeLessThanOrEqual(page.viewportSize()!.width);
  await back.focus();
  await expect(back).toBeFocused();
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL('/library');
  expect(await page.evaluate(() => history.length)).toBe(length);
  await page.goForward();
  await expect(page).toHaveURL('/video-games');
  await expect(back).toBeVisible();
  await page.goBack();
  await expect(page).toHaveURL('/library');
  await page.getByRole('link', { name: 'Add board games', exact: true }).click();
  await expect(page).toHaveURL('/board-games');
  await page.getByRole('button', { name: 'Back to Library', exact: true }).click();
  await expect(page).toHaveURL('/library');
  await page.goForward();
  await expect(page).toHaveURL('/board-games');
});

test('uses one consistent Manage return for each catalog/platforms without a loop', async ({ page }) => {
  await page.goto('/manage');
  for (const name of [/video game catalog/i, /board game catalog/i, /platform shelf/i]) {
    await expect(page.locator('app-back-button')).toHaveCount(0);
    await page.getByRole('link', { name }).click();
    await expect(page.locator('app-back-button')).toHaveCount(1);
    await expect(page.getByRole('button', { name: 'Back to Manage', exact: true })).toBeVisible();
    const length = await page.evaluate(() => history.length);
    await page.getByRole('button', { name: 'Back to Manage', exact: true }).click();
    await expect(page).toHaveURL('/manage');
    expect(await page.evaluate(() => history.length)).toBe(length);
  }
});

test('direct entry and reload replace the leaf with a deterministic internal parent', async ({ page }) => {
  for (const path of ['/video-games', '/board-games', '/platforms']) {
    await page.goto(path + '?returnTo=https://evil.example');
    const length = await page.evaluate(() => history.length);
    await page.getByRole('button', { name: 'Back to Manage', exact: true }).click();
    await expect(page).toHaveURL('/manage');
    expect(await page.evaluate(() => history.length)).toBe(length);
  }
  await primary(page, 'Library');
  await page.getByRole('link', { name: 'Add video games', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Back to Library', exact: true })).toBeVisible();
  await page.reload();
  await page.getByRole('button', { name: 'Back to Manage', exact: true }).click();
  await expect(page).toHaveURL('/manage');
  await page.goBack();
  await expect(page).toHaveURL('/library'); // No replaced catalog entry to loop back into.
});

test('adds a mobile Play Log return but no redundant desktop/top-level Back', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('app-back-button')).toHaveCount(0);
  await page.getByRole('link', { name: /review recent plays/i }).click();
  const mobile = page.viewportSize()!.width < 760;
  const back = page.getByRole('button', { name: 'Back to Home', exact: true });
  if (mobile) {
    await expect(back).toBeVisible();
    await back.click();
    await expect(page).toHaveURL('/');
  } else {
    await expect(back).toBeHidden();
    await primary(page, 'Home');
  }
  await page.goto('/play-log');
  if (mobile) {
    const length = await page.evaluate(() => history.length);
    await back.click();
    await expect(page).toHaveURL('/');
    expect(await page.evaluate(() => history.length)).toBe(length);
  } else {
    await expect(back).toBeHidden();
  }
});

test('health has an accessible exit and a logged-out fallback still runs authGuard', async ({ page }) => {
  await page.goto('/health');
  const back = page.getByRole('button', { name: 'Back to Home', exact: true });
  await expect(back).toBeVisible();
  await back.focus();
  await page.keyboard.press('Space');
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  await page.goto('/health');
  await page.evaluate(() => {
    localStorage.removeItem('game-library-e2e-session');
    window.dispatchEvent(new StorageEvent('storage', { key: 'game-library-e2e-session', newValue: null }));
  });
  await back.click();
  await expect(page).toHaveURL('/login');
  await expect(page.getByLabel('Password', { exact: true })).toBeVisible();
  await expect(page.locator('app-back-button')).toHaveCount(0);
  await page.goto('/platforms');
  await expect(page).toHaveURL('/login');
  await expect(page.getByRole('heading', { name: 'Platforms', exact: true })).toHaveCount(0);
});
