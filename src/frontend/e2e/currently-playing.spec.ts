import { expect, test } from '@playwright/test';

test('Select Game persists both Currently Playing slots on Home after reload', async ({ page, request }) => {
  // Existing guarded fake auth and a dedicated loopback E2E PostgreSQL database only.
  const reset = await request.post('http://127.0.0.1:5218/e2e/reset');
  expect(reset.ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Email').fill('player@example.com');
  await page.getByLabel('Password').fill('password123');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  await expect(page.getByText('No video game selected.', { exact: true })).toBeVisible();
  await expect(page.getByText('No board game selected.', { exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  const current = page.getByRole('article', { name: 'Current pick' });
  await expect(current.getByRole('heading', { name: 'Zelda: Tears of the Kingdom', exact: true })).toBeVisible();
  await current.getByRole('button', { name: 'Select Game', exact: true }).click();
  await expect(current.getByText('Selected for Currently Playing. Find it on Home.', { exact: true })).toBeVisible();

  await page.getByLabel('Mode').selectOption('BoardGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(current.getByRole('heading', { name: 'Cascadia', exact: true })).toBeVisible();
  await current.getByRole('button', { name: 'Select Game', exact: true }).click();
  await expect(current.getByRole('button', { name: 'Selected', exact: true })).toBeDisabled();

  await page.getByRole('link', { name: 'Home', exact: true }).first().click();
  const video = page.getByRole('article', { name: 'Currently Playing Video game', exact: true });
  const board = page.getByRole('article', { name: 'Currently Playing Board game', exact: true });
  await expect(video).toContainText('Zelda: Tears of the Kingdom');
  await expect(board).toContainText('Cascadia');
  await page.reload();
  await expect(video).toContainText('Zelda: Tears of the Kingdom');
  await expect(board).toContainText('Cascadia');

  // A new Picker session cannot propose either active game, even after reload.
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'No owned games match', exact: true })).toBeVisible();
});
