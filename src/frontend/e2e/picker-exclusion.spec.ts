import { expect, test } from '@playwright/test';

test('Library exclusion blocks Picker draws and stale recovery; re-inclusion preserves shown rounds', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Email').fill('player@example.com');
  await page.getByLabel('Password', { exact: true }).fill('password123');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();

  async function library() {
    await page.getByRole('link', { name: 'Library', exact: true }).first().click();
    await expect(page.getByRole('heading', { name: /browse the shelf/i })).toBeVisible();
  }
  const videoName = 'Zelda: Tears of the Kingdom';
  const boardName = 'Cascadia';
  const video = page.locator('.library-card').filter({ hasText: videoName });
  const board = page.locator('.library-card').filter({ hasText: boardName });
  async function picker() {
    await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
    await expect(page.getByLabel('Mode')).toHaveValue('All');
    await expect(page.getByRole('button', { name: 'Pick a game', exact: true })).toBeEnabled();
  }
  async function mode(value: string) {
    await page.getByLabel('Mode').selectOption(value);
    await expect(page.getByLabel('Mode')).toHaveValue(value);
    await expect(page.getByRole('button', { name: 'Pick a game', exact: true })).toBeEnabled();
  }

  await library();
  for (const card of [video, board]) {
    await card.getByRole('button', { name: 'Exclude from Picker', exact: true }).click();
    await expect(card.getByRole('button', { name: 'Include in Picker', exact: true })).toBeEnabled();
    await expect(card.getByText('Excluded from Picker', { exact: true })).toBeVisible();
  }
  await page.reload();
  await expect(video.getByText('Excluded from Picker', { exact: true })).toBeVisible();
  await expect(board.getByText('Excluded from Picker', { exact: true })).toBeVisible();
  await picker();
  for (const value of ['All', 'VideoGames', 'BoardGames']) {
    await mode(value);
    await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'No owned games match', exact: true })).toBeVisible();
    await expect(page.getByRole('article', { name: 'Current pick', exact: true })).toHaveCount(0);
  }

  await library();
  await video.getByRole('button', { name: 'Include in Picker', exact: true }).click();
  await expect(video.getByRole('button', { name: 'Exclude from Picker', exact: true })).toBeEnabled();
  await picker();
  await mode('VideoGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  const current = page.getByRole('article', { name: 'Current pick', exact: true });
  await expect(current.getByRole('heading', { name: videoName, exact: true })).toBeVisible();

  await library();
  await video.getByRole('button', { name: 'Exclude from Picker', exact: true }).click();
  await expect(video.getByRole('button', { name: 'Include in Picker', exact: true })).toBeEnabled();
  await picker();
  await mode('VideoGames');
  await expect(current).toHaveCount(0);
  await expect(page.getByText('The saved proposal is no longer eligible.', { exact: false })).toBeVisible();
  const history = page.getByRole('region', { name: 'Shown history', exact: true });
  await expect(history.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await expect(history.getByRole('button', { name: 'Select Game', exact: true })).toHaveCount(0);

  await library();
  for (const card of [video, board]) {
    await card.getByRole('button', { name: 'Include in Picker', exact: true }).click();
    await expect(card.getByRole('button', { name: 'Exclude from Picker', exact: true })).toBeEnabled();
  }
  await picker();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await expect(current.getByRole('heading', { name: videoName, exact: true })).toBeVisible();
  await expect(current.getByRole('button', { name: 'Select Game', exact: true })).toBeEnabled();
  await page.getByRole('button', { name: 'Another', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'All matching games already shown', exact: true })).toBeVisible();
  await mode('BoardGames');
  await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
  await expect(current.getByRole('heading', { name: boardName, exact: true })).toBeVisible();
});
