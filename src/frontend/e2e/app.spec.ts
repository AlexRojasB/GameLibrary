import { expect, test } from '@playwright/test';

const email = 'player@example.com';
const password = 'password123';

test.beforeEach(async ({ page, request }) => {
  const response = await request.post('http://localhost:5218/e2e/reset');
  expect(response.ok(), `E2E reset failed with ${response.status()} ${response.statusText()}: ${await response.text()}`).toBeTruthy();

  await page.goto('/login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
});

test('navigates the core Play Shelf flows', async ({ page }) => {
  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: /browse the shelf/i })).toBeVisible();
  await expect(page.getByText('Zelda: Tears of the Kingdom')).toBeVisible();
  await expect(page.getByText('Cascadia')).toBeVisible();

  await page.getByRole('button', { name: 'Advanced filters' }).click();
  await expect(page.getByLabel('Game type')).toBeVisible();
  await page.getByLabel('Search').fill('cascadia');
  await expect(page.getByText('Zelda: Tears of the Kingdom')).toBeHidden();
  await expect(page.getByText('Cascadia')).toBeVisible();
});

test('opens management forms in dialogs', async ({ page }) => {
  await page.getByRole('link', { name: 'Manage' }).first().click();
  await expect(page.getByRole('heading', { name: /keep the shelf tidy/i })).toBeVisible();

  await page.getByRole('link', { name: /video game catalog/i }).click();
  await page.getByRole('button', { name: 'Add video game' }).click();
  await expect(page.getByRole('dialog', { name: 'Add video game' })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(page.getByRole('dialog', { name: 'Add video game' })).toBeHidden();
});

test('picks a game and keeps visible session history', async ({ page }) => {
  await page.getByRole('link', { name: 'Pick' }).first().click();
  await expect(page.getByRole('heading', { name: /let the shelf decide/i })).toBeVisible();

  await page.getByRole('button', { name: 'Pick a game' }).click();
  await expect(page.getByRole('heading', { name: /zelda|cascadia/i })).toBeVisible();

  await page.getByRole('button', { name: 'Another' }).click();
  await expect(page.getByRole('heading', { name: /zelda|cascadia/i })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();

  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await page.getByLabel('Mode').selectOption('BoardGames');
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();
});
