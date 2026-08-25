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
  await expect(page.getByRole('heading', { name: /zelda|cascadia/i })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();

  await page.getByRole('button', { name: 'Filter this pick' }).click();
  await page.getByLabel('Mode').selectOption('BoardGames');
  await expect(page.getByRole('heading', { name: 'Previous picks' })).toBeVisible();
});

async function dateTimeLocalOneHourAgo(page: Page): Promise<string> {
  return page.evaluate(() => {
    const date = new Date(Date.now() - 60 * 60 * 1000);
    const pad = (value: number) => value.toString().padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
  });
}
