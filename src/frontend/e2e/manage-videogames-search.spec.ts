import { expect, test } from '@playwright/test';

test('searches Manage VideoGames by partial title, clears and edits without losing search', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Email').fill('player@example.com');
  await page.getByLabel('Password', { exact: true }).fill('password123');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  await page.getByRole('link', { name: 'Manage', exact: true }).first().click();
  await page.getByRole('link', { name: /video game catalog/i }).click();

  const cards = page.locator('.video-games__item');
  const search = page.getByRole('searchbox', { name: 'Search video games', exact: true });
  const clear = page.getByRole('button', { name: 'Clear search', exact: true });
  await expect(cards.filter({ hasText: 'Zelda: Tears of the Kingdom' })).toHaveCount(1);
  const initialCount = await cards.count();
  await expect(search).toBeVisible();
  await expect(clear).toBeDisabled();
  const inputBox = await search.boundingBox();
  expect(inputBox!.width).toBeGreaterThan(150);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await search.fill('  tEaRs  ');
  await expect(cards).toHaveCount(1);
  await expect(cards.first()).toContainText('Zelda: Tears of the Kingdom');
  await expect(page.getByRole('status').filter({ hasText: '1 video game found' })).toBeVisible();

  // The reused Library response must still contain all data needed for editing.
  await cards.first().getByRole('button', { name: 'Edit', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Edit video game', exact: true });
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('Zelda: Tears of the Kingdom');
  await dialog.getByLabel('Name', { exact: true }).fill('Zelda: Tears search edition');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(cards).toHaveCount(1);
  await expect(cards.first()).toContainText('Zelda: Tears search edition');
  await expect(search).toHaveValue('  tEaRs  ');

  await search.fill('no-such-title');
  await expect(page.getByRole('status').filter({ hasText: 'No video games match your search' })).toBeVisible();
  await expect(cards).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Add video game', exact: true })).toBeVisible();
  await clear.click();
  await expect(search).toBeFocused();
  await expect(search).toHaveValue('');
  await expect(cards).toHaveCount(initialCount);
  await expect(cards.filter({ hasText: 'Zelda: Tears search edition' })).toHaveCount(1);

  await search.fill('   ');
  await expect(cards).toHaveCount(initialCount);
  // Matching BoardGames in Library must not appear in Manage VideoGames.
  await search.fill('cAsCaD');
  await expect(page.getByRole('status').filter({ hasText: 'No video games match your search' })).toBeVisible();
});
