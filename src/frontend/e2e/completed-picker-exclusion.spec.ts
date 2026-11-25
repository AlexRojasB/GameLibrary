import { expect, test } from '@playwright/test';

test('Completed status blocks video recovery/draws without changing manual exclusion or shown history', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Email').fill('player@example.com');
  await page.getByLabel('Password').fill('password123');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  const videoName = 'Zelda: Tears of the Kingdom';
  const current = page.getByRole('article', { name: 'Current pick', exact: true });
  const history = page.getByRole('region', { name: 'Shown history', exact: true });

  async function picker() {
    await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
    await expect(page.getByLabel('Mode')).toHaveValue('All');
    await expect(page.getByRole('button', { name: 'Pick a game', exact: true })).toBeEnabled();
  }
  async function editStatus(status: string) {
    await page.getByRole('link', { name: 'Manage', exact: true }).first().click();
    await page.getByRole('link', { name: /video game catalog/i }).click();
    const item = page.locator('.video-games__item').filter({ hasText: videoName });
    await item.getByRole('button', { name: 'Edit', exact: true }).click();
    const dialog = page.getByRole('dialog', { name: 'Edit video game', exact: true });
    await dialog.getByLabel('Game status', { exact: true }).selectOption({ label: status });
    await dialog.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(dialog).toBeHidden();
    await expect(item.getByText(status, { exact: true })).toBeVisible();
  }

  await picker();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(current.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await editStatus('Completed');
  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  const libraryVideo = page.locator('.library-card').filter({ hasText: videoName });
  await expect(libraryVideo).toContainText('Status: Completed');
  await expect(libraryVideo).toContainText('Progress: 100%');
  await expect(libraryVideo.getByRole('button', { name: 'Exclude from Picker', exact: true })).toBeEnabled();
  await expect(libraryVideo.getByText('Excluded from Picker', { exact: true })).toHaveCount(0);

  await picker();
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(current.getByRole('heading', { name: 'Cascadia', exact: true })).toBeVisible();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await expect(page.getByRole('button', { name: 'Pick a game', exact: true })).toBeEnabled();
  await expect(current).toHaveCount(0);
  await expect(page.getByText('The saved proposal is no longer eligible.', { exact: false })).toBeVisible();
  await expect(history.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await expect(history).toContainText('Completed');
  await expect(history.getByRole('button', { name: 'Select Game', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'No owned games match', exact: true })).toBeVisible();
  await expect(history.getByRole('heading', { name: videoName, exact: true })).toBeVisible();

  await editStatus('Playing');
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  await expect(current.getByRole('heading', { name: 'Cascadia', exact: true })).toBeVisible();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'All matching games already shown', exact: true })).toBeVisible();
  await expect(history.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Start over', exact: true }).click();
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(current.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await expect(current).toContainText('Progress: 100%');
  await expect(current.getByRole('button', { name: 'Select Game', exact: true })).toBeEnabled();
});
