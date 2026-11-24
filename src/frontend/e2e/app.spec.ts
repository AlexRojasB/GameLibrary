import { expect, test, type Page } from '@playwright/test';

const email = 'player@example.com';
const password = 'password123';

test.beforeEach(async ({ page, request }) => {
  await page.route('https://e2e.example.test/covers/**', async (route) => {
    await route.fulfill({
      contentType: 'image/png',
      body: Buffer.from(
        'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=',
        'base64',
      ),
    });
  });

  const response = await request.post('http://127.0.0.1:5218/e2e/reset');
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

test('logs a play from Library and opens Play Log from Home', async ({ page }) => {
  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  await expect(page.getByRole('heading', { name: /browse the shelf/i })).toBeVisible();

  const zeldaCard = page.locator('.library-card').filter({ hasText: 'Zelda: Tears of the Kingdom' });
  await zeldaCard.getByRole('button', { name: 'Log play' }).click();
  const dialog = page.getByRole('dialog', { name: 'Zelda: Tears of the Kingdom' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Played date/time').fill(await dateTimeLocalOneHourAgo(page));
  await dialog.getByLabel(/Duration minutes/i).fill('90');
  await dialog.getByRole('button', { name: 'Log play' }).click();
  await expect(zeldaCard.getByText('Play logged.')).toBeVisible();

  await page.getByRole('link', { name: 'Home', exact: true }).first().click();
  await page.locator('.home-card').filter({ hasText: 'Play Log' }).click();
  await expect(page.getByRole('heading', { name: /recent plays from your shelf/i })).toBeVisible();
  await expect(page.getByText('Zelda: Tears of the Kingdom')).toBeVisible();
  await expect(page.getByText('Duration: 1 h 30 min')).toBeVisible();

  const playLogCard = page.locator('.play-log-card').filter({ hasText: 'Zelda: Tears of the Kingdom' });
  await playLogCard.getByRole('button', { name: 'Edit' }).click();
  await expect(dialog).toBeVisible();
  await dialog.getByLabel(/Duration minutes/i).fill('120');
  await dialog.getByRole('button', { name: 'Save' }).click();
  await expect(playLogCard.getByText('Duration: 2 h')).toBeVisible();
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

test('searches and selects a cover during VideoGame creation', async ({ page }) => {
  let searchRequests = 0;
  await page.route('**/cover-images/search', async (route) => {
    if (route.request().method() === 'POST') {
      searchRequests++;
    }
    await route.continue();
  });

  await page.getByRole('link', { name: 'Manage' }).first().click();
  await page.getByRole('link', { name: /video game catalog/i }).click();
  await page.getByRole('button', { name: 'Add video game' }).click();

  const dialog = page.getByRole('dialog', { name: 'Add video game' });
  await expect(dialog).toBeVisible();
  await dialog.getByLabel('Name').fill('Metroid Prime 4');
  await dialog.getByLabel('Switch').check();
  expect(searchRequests).toBe(0);

  await dialog.getByRole('button', { name: 'Search cover' }).click();
  await expect(dialog.locator('.cover-search-card')).toHaveCount(5);
  expect(searchRequests).toBe(1);

  await dialog.locator('.cover-search-card').first().click();
  await expect(dialog.getByLabel('Cover image URL')).toHaveValue('https://e2e.example.test/covers/metroid-prime-4-1.jpg');

  await dialog.getByRole('button', { name: 'Create' }).click();
  await expect(dialog).toBeHidden();

  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  const card = page.locator('.library-card').filter({ hasText: 'Metroid Prime 4' });
  await expect(card).toBeVisible();
  await expect(card.locator('img.library-card__cover')).toHaveAttribute(
    'src',
    'https://e2e.example.test/covers/metroid-prime-4-1.jpg',
  );
});

test('picks a game and keeps visible session history', async ({ page }) => {
  await page.getByRole('link', { name: 'Pick' }).first().click();
  await expect(page.getByRole('heading', { name: /let the shelf decide/i })).toBeVisible();

  await page.getByRole('button', { name: 'Pick a game' }).click();
  await expect(page.getByRole('heading', { name: /zelda|cascadia/i })).toBeVisible();

  await page.getByRole('button', { name: 'Another' }).click();
  await expect(page.locator('.random-picker-card--history')).toHaveCount(1);
  await expect(page.getByRole('article', { name: 'Current pick' }).getByRole('heading', { name: /zelda|cascadia/i })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();

  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await page.getByLabel('Mode').selectOption('BoardGames');
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();
});

test('classifies games, filters the Library, and preserves family picker history', async ({ page }) => {
  await page.getByRole('link', { name: 'Manage' }).first().click();
  await page.getByRole('link', { name: /video game catalog/i }).click();
  await page.getByRole('button', { name: 'Add video game' }).click();
  const videoDialog = page.getByRole('dialog', { name: 'Add video game' });
  await videoDialog.getByLabel('Name').fill('Unclassified Video');
  await videoDialog.getByLabel('Switch').check();
  await expect(videoDialog.getByLabel('Family friendly', { exact: true })).not.toBeChecked();
  await videoDialog.getByRole('button', { name: 'Create' }).click();
  await expect(videoDialog).toBeHidden();
  const video = page.locator('.video-games__item').filter({ hasText: 'Unclassified Video' });
  await expect(video).toBeVisible();
  await expect(video.getByText('Family friendly', { exact: true })).toHaveCount(0);

  await page.getByRole('link', { name: 'Manage' }).first().click();
  await page.getByRole('link', { name: /board game catalog/i }).click();
  await page.getByRole('button', { name: 'Add board game' }).click();
  const boardDialog = page.getByRole('dialog', { name: 'Add board game' });
  await boardDialog.getByLabel('Name').fill('Family Tabletop');
  await boardDialog.getByLabel('Minimum players').fill('1');
  await boardDialog.getByLabel('Maximum players').fill('4');
  await expect(boardDialog.getByLabel('Family friendly', { exact: true })).not.toBeChecked();
  await boardDialog.getByLabel('Family friendly', { exact: true }).check();
  await boardDialog.getByRole('button', { name: 'Create' }).click();
  await expect(boardDialog).toBeHidden();
  await expect(page.locator('.board-games__item').filter({ hasText: 'Family Tabletop' })
    .getByText('Family friendly', { exact: true })).toBeVisible();

  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  await expect(page.locator('.library-card').filter({ hasText: 'Family Tabletop' })
    .getByText('Family friendly', { exact: true })).toBeVisible();
  await expect(page.locator('.library-card').filter({ hasText: 'Unclassified Video' })
    .getByText('Family friendly', { exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Advanced filters' }).click();
  await page.getByLabel('Family friendly only').check();
  await expect(page.locator('.library-card')).toHaveCount(1);
  await expect(page.locator('.library-card')).toContainText('Family Tabletop');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);

  await page.getByRole('link', { name: 'Pick' }).first().click();
  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await page.getByLabel('Family friendly only').check();
  await page.getByRole('button', { name: 'Pick a game' }).click();
  const current = page.getByRole('article', { name: 'Current pick' });
  await expect(current).toContainText('Family Tabletop');
  await expect(current.getByText('Family friendly', { exact: true })).toBeVisible();

  await page.getByLabel('Family friendly only').uncheck();
  await expect(page.locator('.random-picker-card--history')).toHaveCount(1);
  await page.getByLabel('Family friendly only').check();
  await page.getByLabel('Mode').selectOption('VideoGames');
  await expect(page.getByLabel('Family friendly only')).toBeChecked();
  await page.getByRole('button', { name: 'Pick a game' }).click();
  await expect(page.getByRole('heading', { name: 'No owned games match' })).toBeVisible();
  await expect(page.locator('.random-picker-card--history')).toContainText('Family Tabletop');

  await page.getByLabel('Mode').selectOption('BoardGames');
  await page.getByRole('button', { name: 'Pick a game' }).click();
  await expect(page.getByRole('heading', { name: 'All matching games already shown' })).toBeVisible();
  await expect(page.locator('.random-picker-card--history')).toHaveCount(1);
  await page.getByRole('button', { name: 'Start over' }).click();
  await expect(page.locator('.random-picker-card--history')).toHaveCount(0);
  await page.getByRole('button', { name: 'Pick a game' }).click();
  await expect(current).toContainText('Family Tabletop');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('preserves an existing Steam link, previews library, and imports selected games', async ({ page }) => {
  await page.goto('/manage/steam');
  await expect(page.getByRole('heading', { name: 'Steam Library' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Link Steam account', exact: true })).toHaveCount(0);
  // The isolated host now explicitly admits v2, never the ordinary shipped host.
  await expect(page.getByText('Steam linking is temporarily unavailable.', { exact: false })).toHaveCount(0);
  await expect(page.getByText('SteamID64: 76561198000000000')).toBeVisible();

  await page.getByRole('button', { name: 'Preview Steam library' }).click();
  await expect(page.getByText('Dota 2')).toBeVisible();
  await expect(page.getByText('Counter-Strike 2')).toBeVisible();
  await expect(page.getByText('Team Fortress 2')).toBeVisible();

  const candidateCheckboxes = page.locator('.steam-candidate input[type="checkbox"]');
  await candidateCheckboxes.nth(0).check();
  await candidateCheckboxes.nth(1).check();

  await page.getByRole('button', { name: 'Import selected' }).click();

  await expect(page.getByText('Import Summary')).toBeVisible();
  await expect(page.getByText('Imported: 2')).toBeVisible();
  await expect(candidateCheckboxes.nth(0)).toBeDisabled();
  await expect(candidateCheckboxes.nth(1)).toBeDisabled();
  await expect(page.getByRole('button', { name: 'Import selected' })).toBeDisabled();
  await page.getByRole('button', { name: 'Select all', exact: true }).click();
  await expect(candidateCheckboxes.nth(0)).not.toBeChecked();
  await expect(candidateCheckboxes.nth(1)).not.toBeChecked();
  await expect(candidateCheckboxes.nth(2)).toBeChecked();

  await page.goto('/library');
  await expect(page.locator('.library-card').filter({ hasText: 'Dota 2' })).toBeVisible();
  await expect(page.locator('.library-card').filter({ hasText: 'Counter-Strike 2' })).toBeVisible();
  await expect(page.locator('.library-card').filter({ hasText: 'Zelda: Tears of the Kingdom' })).toBeVisible();
  await page.goto('/play-log');
  await expect(page.getByRole('heading', { name: 'No plays logged yet.' })).toBeVisible();
});

test('rejects transferred legacy URLs and offers only v2 after an explicit unlink in the isolated host', async ({ page, browser }) => {
  await page.goto('/manage/steam');
  const other = await browser.newContext();
  try {
    for (const path of ['/steam/link/start/synthetic-transferred-state', '/steam/link/callback?state=synthetic-transferred-state']) {
      const response = await other.request.get('http://127.0.0.1:5218' + path, { maxRedirects: 0 });
      expect(response.status()).toBe(410);
      expect(response.headers()['location']).toBeUndefined();
      expect(response.headers()['set-cookie']).toBeUndefined();
    }
    expect(await other.cookies('http://127.0.0.1:5218')).toEqual([]);
  } finally {
    await other.close();
  }
  await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Unlink', exact: true }).click();
  await expect(page.getByText('No Steam Account Linked')).toBeVisible();
  await expect(page.getByRole('button', { name: 'Link Steam account', exact: true })).toBeVisible();
});

async function dateTimeLocalOneHourAgo(page: Page): Promise<string> {
  return page.evaluate(() => {
    const date = new Date(Date.now() - 60 * 60 * 1000);
    const pad = (value: number) => value.toString().padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  });
}
