import { expect, test } from '@playwright/test';

test('Another survives leaving Picker, reload and sign-in with proposal, history and filters', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  async function signIn() {
    await page.goto('/login');
    await page.getByLabel('Email').fill('player@example.com');
    await page.getByLabel('Password').fill('password123');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  }
  await signIn();
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await page.getByRole('button', { name: 'Filter this pick' }).click();
  const players = page.getByLabel('Player count', { exact: true });
  await players.fill('1');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  const current = page.getByRole('article', { name: 'Current pick', exact: true });
  await expect(current).toBeVisible();
  const first = await current.getByRole('heading').innerText();
  await page.getByRole('button', { name: 'Another', exact: true }).click();
  await expect(current.getByRole('heading')).not.toHaveText(first);
  const second = await current.getByRole('heading').innerText();
  const history = page.getByRole('region', { name: 'Shown history', exact: true });
  await expect(history.getByRole('heading', { name: first, exact: true })).toBeVisible();

  async function expectRecovered() {
    await expect(current.getByRole('heading')).toHaveText(second);
    await expect(history.getByRole('heading', { name: first, exact: true })).toBeVisible();
    await expect(page.getByLabel('Mode')).toHaveValue('All');
    await expect(page.getByText('1 picker filter active.', { exact: true })).toBeVisible();
    await expect(history.getByRole('button', { name: 'Select Game', exact: true })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Skip', exact: true })).toHaveCount(0);
  }
  await page.getByRole('link', { name: 'Home', exact: true }).first().click();
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await expectRecovered();
  await page.reload();
  await expectRecovered();
  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await expect(players).toHaveValue('1');
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await signIn();
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await expectRecovered();
});
