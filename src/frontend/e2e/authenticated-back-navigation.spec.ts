import { expect, test } from '@playwright/test';

import { expectNoLoginActivation, expectValidSession, mockAuthenticatedNavigation, primary, signIn, watchLoginActivations } from './fixtures/authenticated-navigation';

test.beforeEach(async ({ context }) => { await mockAuthenticatedNavigation(context); });

test('ordinary sign-in replaces the form and Back/Forward preserves history and session', async ({ page }) => {
  await page.goto('/health');
  await page.goto('/login');
  const length = await page.evaluate(() => history.length);
  await signIn(page);
  await expectValidSession(page);
  expect(await page.evaluate(() => history.length)).toBe(length);
  await watchLoginActivations(page);
  for (let i = 0; i < 3; i++) {
    await page.goBack();
    await expect(page).toHaveURL('/health');
    await expectValidSession(page);
    await page.goForward();
    await expect(page).toHaveURL('/');
    await expectValidSession(page);
    expect(await page.evaluate(() => history.length)).toBe(length);
  }
  await expectNoLoginActivation(page);
});

test('legacy persisted recovery confirmation cannot reopen Sign in on Back or reload', async ({ page }) => {
  await page.goto('/login');
  await signIn(page);
  // Model a pre-fix entry left by recovery, without putting a code into history.
  await page.evaluate(() => history.pushState({ navigationId: 100, passwordResetComplete: true }, '', '/login'));
  await page.goto('/library');
  await expect(page.getByRole('heading', { name: 'Browse the shelf.', exact: true })).toBeVisible();
  const length = await page.evaluate(() => history.length);
  await watchLoginActivations(page);
  await page.goBack();
  await expect(page).toHaveURL('/');
  await expectValidSession(page);
  await expectNoLoginActivation(page);
  for (let i = 0; i < 2; i++) {
    await page.goForward();
    await expect(page).toHaveURL('/library');
    await page.goBack();
    await expect(page).toHaveURL('/');
    expect(await page.evaluate(() => history.length)).toBe(length);
  }
  await expectNoLoginActivation(page);
  await page.evaluate(() => history.replaceState({ ...history.state, passwordResetComplete: true }, '', '/login'));
  await page.reload();
  await expect(page).toHaveURL('/');
  await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toHaveCount(0);
  await expectValidSession(page);
});

test('restored sessions, direct guest links and legitimate in-app Back/Forward remain valid', async ({ page }) => {
  await page.goto('/login');
  await signIn(page);
  await page.goto('/library');
  await expect(page.getByRole('heading', { name: 'Browse the shelf.', exact: true })).toBeVisible();
  await page.reload();
  await expectValidSession(page);
  await watchLoginActivations(page);
  await primary(page, 'Manage');
  const length = await page.evaluate(() => history.length);
  for (let i = 0; i < 2; i++) {
    await page.goBack();
    await expect(page).toHaveURL('/library');
    await page.goForward();
    await expect(page).toHaveURL('/manage');
  }
  expect(await page.evaluate(() => history.length)).toBe(length);
  await expectNoLoginActivation(page);
  for (const guest of ['/login', '/register']) {
    await page.goto(guest);
    await expect(page).toHaveURL('/');
    await expectValidSession(page);
    await expect(page.locator('app-login, app-register')).toHaveCount(0);
  }
});

test('signed-out direct links and logout Back/Forward do not bypass protected guards', async ({ page }) => {
  await page.goto('/library');
  await expect(page).toHaveURL('/login');
  await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toBeVisible();
  await signIn(page);
  await primary(page, 'Library');
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Sign in', exact: true })).toBeVisible();
  const length = await page.evaluate(() => history.length);
  for (let i = 0; i < 2; i++) {
    await page.goBack();
    await expect(page).toHaveURL('/login');
    await expect(page.locator('app-home, app-library-page')).toHaveCount(0);
    expect(await page.evaluate(() => localStorage.getItem('game-library-e2e-session'))).toBeNull();
    await page.goForward();
    await expect(page).toHaveURL('/login');
    expect(await page.evaluate(() => history.length)).toBe(length);
  }
  await page.goto('/');
  await expect(page).toHaveURL('/login');
  expect(await page.evaluate(async () => (await fetch('http://127.0.0.1:5218/auth/me')).status)).toBe(401);
});
