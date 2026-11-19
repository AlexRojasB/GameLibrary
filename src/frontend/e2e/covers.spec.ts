import { expect, test, type APIResponse, type Locator, type Page, type Request, type Route } from '@playwright/test';

import { coverFixture } from './cover-fixture';

const api = 'http://127.0.0.1:5218';

test.beforeEach(async ({ page, request }) => {
  const reset = await request.post(`${api}/e2e/reset`);
  expect(reset.status()).toBe(204);
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill('player@example.com');
  await page.getByLabel('Password', { exact: true }).fill('password123');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
});

test('uploads, crops, replaces and removes a private VideoGame cover without changing metadata', async ({ page }) => {
  test.setTimeout(120_000);
  const contentRequests: { url: string; authorized: boolean }[] = [];
  page.on('request', (request) => {
    if (/\/cover-uploads\/.+\/content$/.test(request.url())) {
      contentRequests.push({ url: request.url(), authorized: !!request.headers()['authorization'] });
    }
  });
  await page.goto('/video-games');
  await page.getByRole('button', { name: 'Add video game', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Add video game' });
  await dialog.getByLabel('Name', { exact: true }).fill('Private Portrait');
  await dialog.getByLabel('Switch', { exact: true }).check();
  await dialog.getByLabel('Family friendly', { exact: true }).check();
  let uploads = 0;
  page.on('request', (request) => { if (request.url() === `${api}/cover-uploads` && request.method() === 'POST') uploads++; });
  await editLocal(page, dialog);
  await expect(dialog.getByRole('button', { name: 'Crop 2:3', exact: true })).toHaveAttribute('aria-pressed', 'true');
  await dialog.getByLabel('Zoom', { exact: true }).focus();
  await page.keyboard.press('Home');
  await page.keyboard.press('ArrowRight');
  await dialog.getByRole('button', { name: 'Move right', exact: true }).click();
  const frame = dialog.getByRole('group', { name: 'Cover crop. Arrow keys move; Shift and arrow keys resize.', exact: true });
  await frame.focus();
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('Shift+ArrowLeft');
  await dialog.getByRole('button', { name: 'Rotate 90 degrees', exact: true }).click();
  await dialog.getByRole('button', { name: 'Reset', exact: true }).click();
  await useImage(dialog);
  expect(uploads).toBe(0);
  await expect(dialog.getByLabel('Cover image URL', { exact: true })).toHaveValue('');
  const creation = await captureJsonWrite(page, `${api}/video-games`, 'POST');
  await saveUpload(page, dialog, 'Create');
  const createdWrite = await creation.result;
  expect(createdWrite.response.status()).toBe(201);
  const created = createdWrite.json;
  expect(created.coverImageUrl).toBeNull();
  expect(created.coverUploadId).toMatch(/^[\da-f-]{36}$/i);
  expect(createdWrite.request.postDataJSON()).not.toHaveProperty('coverImageUrl');
  expect(createdWrite.request.postDataJSON().coverUpdate).not.toHaveProperty('expectedCover');
  await expect(dialog).toBeHidden();
  let card = page.locator('.video-games__item').filter({ hasText: 'Private Portrait' });
  await expect(card.locator('app-cover-image img')).toHaveAttribute('src', /^blob:/);
  await card.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit video game' });
  await editLocal(page, dialog, true);
  await dialog.getByRole('button', { name: 'Fit whole image', exact: true }).click();
  await useImage(dialog);
  const replacement = await captureJsonWrite(page, `${api}/games/${created.id}/cover`, 'PUT');
  await saveUpload(page, dialog, 'Save');
  const replaced = await replacement.result;
  expect(replaced.response.status()).toBe(200);
  const replacementBody = replaced.json;
  expect(replacementBody.coverUploadId).not.toBe(created.coverUploadId);
  expect(replaced.request.postDataJSON().coverUpdate.expectedCover).toEqual({ coverImageUrl: null, coverUploadId: created.coverUploadId });
  expect(Object.keys(replaced.request.postDataJSON())).toEqual(['coverUpdate']);
  await expect(dialog).toBeHidden();
  await card.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit video game' });
  await dialog.getByRole('button', { name: 'Remove cover', exact: true }).click();
  const removal = page.waitForResponse((response) => response.url() === `${api}/games/${created.id}/cover` && response.request().method() === 'PUT');
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  expect((await removal).status()).toBe(200);
  await expect(dialog).toBeHidden();
  await expect(card.locator('img')).toHaveCount(0);
  await expect(card).toContainText('Family friendly');
  await expect(card).toContainText('Switch');
  await page.getByRole('link', { name: 'Library', exact: true }).first().click();
  card = page.locator('.library-card').filter({ hasText: 'Private Portrait' });
  await expect(card).toBeVisible();
  await expect(card.locator('img')).toHaveCount(0);
  expect(contentRequests.length).toBeGreaterThan(0);
  expect(contentRequests.every((request) => request.authorized && !request.url.includes('?'))).toBe(true);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});

test('BoardGame camera fallback fits whole artwork, retains Ready after failed save and resolves a real cover conflict', async ({ page }) => {
  test.setTimeout(120_000);
  if (test.info().project.name === 'mobile-chrome') await page.setViewportSize({ width: 320, height: 740 });
  await page.goto('/board-games');
  await page.getByRole('button', { name: 'Add board game', exact: true }).click();
  let dialog = page.getByRole('dialog', { name: 'Add board game' });
  await dialog.getByLabel('Name', { exact: true }).fill('Whole Square Artwork');
  await dialog.getByLabel('Minimum players', { exact: true }).fill('1');
  await dialog.getByLabel('Maximum players', { exact: true }).fill('4');
  await dialog.getByLabel('Notes', { exact: true }).fill('Keep these notes');
  await editLocal(page, dialog, true, true);
  await expect(dialog.getByRole('button', { name: 'Fit whole image', exact: true })).toHaveAttribute('aria-pressed', 'true');
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  await useImage(dialog);
  let fail = true;
  await page.route(`${api}/board-games`, async (route) => {
    if (route.request().method() === 'POST' && fail) {
      fail = false;
      await route.fulfill({ status: 400, contentType: 'application/problem+json', body: JSON.stringify({ detail: 'Deterministic game validation failure.' }) });
    } else await route.continue();
  });
  const failedSave = page.waitForResponse((response) => response.url() === `${api}/board-games` && response.request().method() === 'POST');
  await saveUpload(page, dialog, 'Create');
  const failed = await failedSave;
  const ready = failed.request().postDataJSON().coverUpdate.uploadId;
  await expect(dialog.getByRole('alert')).toContainText('Deterministic game validation failure.');
  await expect(dialog.getByLabel('Notes', { exact: true })).toHaveValue('Keep these notes');
  const retry = await captureJsonWrite(page, `${api}/board-games`, 'POST');
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  const retried = await retry.result;
  expect(retried.response.status()).toBe(201);
  expect(retried.request.postDataJSON().coverUpdate.uploadId).toBe(ready);
  const created = retried.json;
  await expect(dialog).toBeHidden();
  const card = page.locator('.board-games__item').filter({ hasText: 'Whole Square Artwork' });
  await card.scrollIntoViewIfNeeded();
  await expect(card.locator('app-cover-image img')).toHaveAttribute('src', /^blob:/);
  const artwork = await card.locator('img').evaluate(async (image: HTMLImageElement) => {
    await image.decode();
    const canvas = document.createElement('canvas');
    canvas.width = image.naturalWidth; canvas.height = image.naturalHeight;
    const context = canvas.getContext('2d')!;
    context.drawImage(image, 0, 0);
    return { width: canvas.width, height: canvas.height, padding: [...context.getImageData(0, 0, 1, 1).data] };
  });
  expect(artwork.height).toBe(artwork.width * 3 / 2);
  expect(artwork.padding.slice(0, 3).every((channel) => channel >= 245)).toBe(true);
  await card.getByRole('button', { name: 'Edit', exact: true }).click();
  dialog = page.getByRole('dialog', { name: 'Edit board game' });
  await dialog.getByLabel('Notes', { exact: true }).fill('Retained conflict edits');
  await dialog.getByLabel('Cover image URL', { exact: true }).fill('https://e2e.example.test/pending.jpg');
  const auth = await authorization(page);
  const otherTab = await page.request.put(`${api}/games/${created.id}/cover`, {
    headers: auth, data: { coverUpdate: { intent: 'remove', expectedCover: { coverImageUrl: null, coverUploadId: ready } } },
  });
  expect(otherTab.status()).toBe(200);
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog.getByRole('button', { name: 'Keep current cover', exact: true })).toBeVisible();
  await expect(dialog.getByLabel('Notes', { exact: true })).toHaveValue('Retained conflict edits');
  await dialog.getByRole('button', { name: 'Keep current cover', exact: true }).click();
  await dialog.getByRole('button', { name: 'Save', exact: true }).click();
  await expect(dialog).toBeHidden();
  const games = await (await page.request.get(`${api}/board-games`, { headers: auth })).json();
  const saved = games.find((game: { id: string }) => game.id === created.id);
  expect(saved.notes).toBe('Retained conflict edits');
  expect(saved.coverImageUrl).toBeNull();
  expect(saved.coverUploadId).toBeNull();
});

test('Escape during delayed upload completion prevents any late Game creation', async ({ page }) => {
  test.setTimeout(120_000);
  await page.goto('/video-games');
  await page.getByRole('button', { name: 'Add video game', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add video game' });
  await dialog.getByLabel('Name', { exact: true }).fill('Canceled Local Cover');
  await dialog.getByLabel('Switch', { exact: true }).check();
  await editLocal(page, dialog);
  await useImage(dialog);
  let complete!: () => void;
  const completion = new Promise<void>((resolve) => { complete = resolve; });
  let ready!: () => void;
  const serverReady = new Promise<void>((resolve) => { ready = resolve; });
  let createRequests = 0;
  page.on('request', (request) => { if (request.url() === `${api}/video-games` && request.method() === 'POST') createRequests++; });
  await page.route(`${api}/cover-uploads`, async (route) => {
    const response = await route.fetch();
    expect(response.status()).toBe(201);
    ready();
    await completion;
    await route.fulfill({ response }).catch(() => undefined); // Browser may have aborted after Escape.
  });
  await dialog.getByRole('button', { name: 'Create', exact: true }).click();
  await serverReady;
  await expect(dialog.getByText('Uploading cover...', { exact: true })).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(dialog).toBeHidden();
  complete();
  await page.unrouteAll({ behavior: 'wait' });
  const games = await (await page.request.get(`${api}/video-games`, { headers: await authorization(page) })).json();
  expect(games.some((game: { name: string }) => game.name === 'Canceled Local Cover')).toBe(false);
  expect(createRequests).toBe(0);
  await page.getByRole('button', { name: 'Add video game', exact: true }).click();
  await expect(dialog.getByLabel('Name', { exact: true })).toHaveValue('');
  await expect(dialog.locator('.cover-search__selected')).toHaveCount(0);
});

test('a committed Create with a lost response is recovered through its receipt without duplicate creation', async ({ page }) => {
  test.setTimeout(120_000);
  await page.goto('/video-games');
  await page.getByRole('button', { name: 'Add video game', exact: true }).click();
  const dialog = page.getByRole('dialog', { name: 'Add video game' });
  await dialog.getByLabel('Name', { exact: true }).fill('Recovered Private Cover');
  await dialog.getByLabel('Switch', { exact: true }).check();
  await editLocal(page, dialog);
  await useImage(dialog);
  let creates = 0;
  await page.route(`${api}/video-games`, async (route) => {
    if (route.request().method() !== 'POST') { await route.continue(); return; }
    creates++;
    const committed = await route.fetch();
    expect(committed.status()).toBe(201);
    await route.fulfill({ status: 503, contentType: 'application/problem+json', body: JSON.stringify({ code: 'cover_outcome_unknown' }) });
  });
  await saveUpload(page, dialog, 'Create');
  await expect(dialog.getByText(/The save may have committed/)).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Create', exact: true })).toBeDisabled();
  await dialog.getByRole('button', { name: 'Check save status', exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(page.locator('.video-games__item').filter({ hasText: 'Recovered Private Cover' })).toHaveCount(1);
  expect(creates).toBe(1);
});

async function captureJsonWrite(page: Page, url: string, method: string) {
  let captured!: (write: { response: APIResponse; request: Request; json: Record<string, unknown> }) => void;
  let failed!: (error: unknown) => void;
  const result = new Promise<{ response: APIResponse; request: Request; json: Record<string, unknown> }>((resolve, reject) => {
    captured = resolve;
    failed = reject;
  });
  const handler = async (route: Route) => {
    if (route.request().method() !== method) { await route.fallback(); return; }
    try {
      // Read the REAL server body before releasing it to the browser. A later
      // navigation cannot invalidate this Node-side response/body snapshot.
      const response = await route.fetch();
      const json = await response.json() as Record<string, unknown>;
      captured({ response, request: route.request(), json });
      await route.fulfill({ response });
      await page.unroute(url, handler);
    } catch (error) { failed(error); throw error; }
  };
  await page.route(url, handler);
  return { result };
}

async function editLocal(page: Page, dialog: Locator, square = false, camera = false) {
  const action = dialog.getByRole('button', { name: camera ? 'Take photo' : 'Upload image', exact: true });
  await expect(action).toBeEnabled();
  const chooser = page.waitForEvent('filechooser');
  await action.click();
  await (await chooser).setFiles(coverFixture(square));
  await expect(dialog.getByRole('region', { name: 'Edit local cover', exact: true })).toBeVisible();
  await expect(dialog.getByRole('button', { name: 'Use image', exact: true })).toBeEnabled();
}

async function useImage(dialog: Locator) {
  await dialog.getByRole('button', { name: 'Use image', exact: true }).click();
  await expect(dialog.getByRole('region', { name: 'Edit local cover', exact: true })).toHaveCount(0);
  await expect(dialog.locator('.cover-search__selected img')).toHaveAttribute('src', /^blob:/);
}

async function saveUpload(page: Page, dialog: Locator, action: string) {
  const result = page.waitForResponse((response) => response.url() === `${api}/cover-uploads` && response.request().method() === 'POST');
  await dialog.getByRole('button', { name: action, exact: true }).click();
  const response = await result;
  if (response.status() === 429) {
    // Exercise an explicit user retry while honoring the real backend limiter, never relax it for E2E.
    const seconds = Number(response.headers()['retry-after'] ?? '60');
    await page.waitForTimeout((seconds + 1) * 1000);
    const retried = page.waitForResponse((next) => next.url() === `${api}/cover-uploads` && next.request().method() === 'POST');
    await dialog.getByRole('button', { name: action, exact: true }).click();
    expect((await retried).status()).toBe(201);
  } else expect(response.status()).toBe(201);
}

async function authorization(page: Page) {
  const token = await page.evaluate(() => JSON.parse(localStorage.getItem('game-library-e2e-session')!).access_token as string);
  return { Authorization: `Bearer ${token}` };
}
