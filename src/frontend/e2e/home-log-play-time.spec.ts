import { expect, test, type Request } from '@playwright/test';

test('selects both game types then explicitly logs from Home without clearing selections or Picker rounds', async ({ page, request }) => {
  expect((await request.post('http://127.0.0.1:5218/e2e/reset')).ok()).toBeTruthy();
  await page.goto('/login');
  await page.getByLabel('Email').fill('player@example.com');
  await page.getByLabel('Password').fill('password123');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
  await page.getByRole('link', { name: 'Pick', exact: true }).first().click();
  const current = page.getByRole('article', { name: 'Current pick', exact: true });
  for (const [mode, name] of [['VideoGames', 'Zelda: Tears of the Kingdom'], ['BoardGames', 'Cascadia']]) {
    await page.getByLabel('Mode').selectOption(mode);
    await page.getByRole('button', { name: 'Pick a game', exact: true }).click();
    await expect(current.getByRole('heading', { name, exact: true })).toBeVisible();
    await expect(current.getByRole('button', { name: /log play/i })).toHaveCount(0);
    await current.getByRole('button', { name: 'Select Game', exact: true }).click();
    await expect(current.getByRole('button', { name: 'Selected', exact: true })).toBeDisabled();
  }

  // Capture authenticated recovery via the real UI transport, not browser DB access.
  // Read the real API body before delivering it to Chromium, whose mobile CDP
  // response resource may be unavailable even before leaving the Picker.
  const roundBodies = new WeakMap<Request, unknown>();
  await page.route('http://127.0.0.1:5218/random-picker/rounds/*', async route => {
    const response = await route.fetch();
    expect(response.status()).toBe(200);
    roundBodies.set(route.request(), await response.json());
    await route.fulfill({ response });
  });
  async function recoverRound(mode: string, action: () => Promise<unknown>) {
    const url = `http://127.0.0.1:5218/random-picker/rounds/${mode}`;
    const recovered = page.waitForResponse(r => r.request().method() === 'GET'
      && r.url() === url);
    const [response] = await Promise.all([recovered, action()]);
    expect(roundBodies.has(response.request())).toBeTruthy();
    return roundBodies.get(response.request());
  }
  const before = new Map<string, unknown>();
  for (const mode of ['All', 'VideoGames', 'BoardGames']) {
    before.set(mode, await recoverRound(mode, () => page.getByLabel('Mode').selectOption(mode)));
  }
  await page.getByRole('link', { name: 'Home', exact: true }).first().click();
  const video = page.getByRole('article', { name: 'Currently Playing Video game', exact: true });
  const board = page.getByRole('article', { name: 'Currently Playing Board game', exact: true });
  await expect(video).toContainText('Zelda: Tears of the Kingdom');
  await expect(board).toContainText('Cascadia');
  const playedLocal = await page.evaluate(() => {
    const d = new Date(Date.now() - 60 * 60 * 1000);
    const pad = (v: number) => String(v).padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  });
  const playedIso = await page.evaluate(value => new Date(value).toISOString(), playedLocal);
  for (const [slot, name, duration] of [[video, 'Zelda: Tears of the Kingdom', 90], [board, 'Cascadia', 45]] as const) {
    await slot.getByRole('button', { name: 'Log Play Time', exact: true }).click();
    const dialog = page.getByRole('dialog', { name, exact: true });
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('Played date/time').fill(playedLocal);
    await dialog.getByLabel(/Duration minutes/).fill(String(duration));
    const saved = page.waitForResponse(r => r.request().method() === 'POST' && r.url().endsWith('/play-log'));
    const refreshed = page.waitForResponse(r => r.request().method() === 'GET' && r.url().endsWith('/currently-playing'));
    await dialog.getByRole('button', { name: 'Log play', exact: true }).click();
    expect((await saved).status()).toBe(201);
    expect((await refreshed).ok()).toBeTruthy();
    await expect(dialog).toBeHidden();
    await expect(page.getByText('Play logged.', { exact: true })).toBeVisible();
    await expect(video).toContainText('Zelda: Tears of the Kingdom');
    await expect(board).toContainText('Cascadia');
  }
  await page.locator('.home-card').filter({ hasText: 'Play Log' }).click();
  for (const [name, duration] of [['Zelda: Tears of the Kingdom', '1 h 30 min'], ['Cascadia', '45 min']]) {
    const card = page.locator('.play-log-card').filter({ hasText: name });
    await expect(card).toContainText(`Duration: ${duration}`);
  }
  // The recorded date is verified through the same authorized list read as Play Log.
  const logs = page.waitForResponse(r => r.request().method() === 'GET' && r.url() === 'http://127.0.0.1:5218/play-log');
  await page.reload();
  const entries = await (await logs).json();
  expect(entries).toHaveLength(2);
  for (const entry of entries) expect(new Date(entry.playedAt).toISOString()).toBe(playedIso);
  const all = await recoverRound('All', () => page.getByRole('link', { name: 'Pick', exact: true }).first().click());
  expect(all).toEqual(before.get('All'));
  for (const mode of ['VideoGames', 'BoardGames']) {
    const recovered = await recoverRound(mode, () => page.getByLabel('Mode').selectOption(mode));
    expect(recovered).toEqual(before.get(mode));
  }
});
