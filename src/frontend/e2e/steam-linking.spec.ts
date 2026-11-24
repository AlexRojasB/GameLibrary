import { createHash, createHmac, randomUUID } from 'node:crypto';
import { expect, test, type BrowserContext, type Page } from '@playwright/test';

const api = 'http://127.0.0.1:5218';
const root = api + '/steam/link/v2/';
const key = 'game-library-steam-link-v2';
const provider = 'https://steamcommunity.com/openid/login';
const ns = 'http://specs.openid.net/auth/2.0';
const signed = 'op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle';
const assertionKey = 'steam-v2-isolated-synthetic-provider'; // Synthetic fixture only.
const steamId = () => (70000000000000000n + BigInt('0x' + randomUUID().replaceAll('-', '').slice(0, 12))).toString();

type Handle = { transactionId: string; initiatingUserId: string; expiresAt: string };
type ProviderFixture = { identity: string; callback: string | null; authorization: string | null; count: number };

function assertion(authorization: string, identity: string): URL {
  const request = new URL(authorization);
  const returnTo = request.searchParams.get('openid.return_to')!;
  const values: Record<string, string> = {
    'openid.ns': ns, 'openid.mode': 'id_res', 'openid.op_endpoint': provider,
    'openid.claimed_id': 'https://steamcommunity.com/openid/id/' + identity,
    'openid.identity': 'https://steamcommunity.com/openid/id/' + identity,
    'openid.return_to': returnTo, 'openid.realm': request.searchParams.get('openid.realm')!,
    'openid.response_nonce': new Date().toISOString().replace(/\.\d{3}Z$/, 'Z') + randomUUID(),
    'openid.assoc_handle': 'synthetic-browser-provider', 'openid.signed': signed,
  };
  values['openid.sig'] = createHmac('sha256', assertionKey).update(Object.keys(values).sort().map(k => k + ':' + values[k]).join('\n')).digest('base64');
  const callback = new URL(returnTo);
  for (const [k, v] of Object.entries(values)) callback.searchParams.set(k, v);
  return callback;
}

async function fakeProvider(context: BrowserContext, identity = steamId()): Promise<ProviderFixture> {
  const fixture: ProviderFixture = { identity, callback: null, authorization: null, count: 0 };
  // Intercept at the provider boundary only. Angular still performs native top-level
  // navigation; the callback hits real Kestrel/Core/PostgreSQL with browser cookies.
  await context.route(provider + '**', async route => {
    fixture.count++;
    const headers = await route.request().allHeaders();
    expect(!headers['authorization'] && !headers['cookie'], 'Provider must receive no application credentials').toBe(true);
    expect(route.request().isNavigationRequest()).toBe(true);
    fixture.authorization = route.request().url();
    const callback = assertion(fixture.authorization, fixture.identity);
    fixture.callback = callback.toString();
    const hidden = [...callback.searchParams].map(([name, value]) => `<input type="hidden" name="${escape(name)}" value="${escape(value)}">`).join('');
    const cancel = new URL(callback.origin + callback.pathname);
    cancel.searchParams.set('state', callback.searchParams.get('state')!);
    cancel.searchParams.set('openid.ns', ns); cancel.searchParams.set('openid.mode', 'cancel');
    await route.fulfill({ contentType: 'text/html', headers: { 'Referrer-Policy': 'no-referrer', 'Cache-Control': 'no-store' }, body:
      `<!doctype html><title>Controlled Steam fixture</title><h1>Controlled Steam fixture</h1><form method="get" action="${callback.origin + callback.pathname}">${hidden}<button>Approve synthetic Steam identity</button></form><a href="${escape(cancel.toString())}">Decline at provider</a>` });
  });
  return fixture;
}
function escape(value: string) { return value.replaceAll('&', '&amp;').replaceAll('"', '&quot;').replaceAll('<', '&lt;'); }
async function signIn(page: Page, email: string) {
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Log out', exact: true }).or(page.getByLabel('Email')).first()).toBeVisible();
  if (await page.getByRole('button', { name: 'Log out', exact: true }).isVisible()) {
    await page.getByRole('button', { name: 'Log out', exact: true }).click();
    await page.waitForURL('**/login');
    // Finish the logout navigation before entering credentials in a new identity's
    // form. Native reload also prevents typing into a departing login instance.
    await page.reload();
  }
  await page.getByLabel('Email').fill(email); await page.getByLabel('Password').fill('password123');
  await expect(page.getByLabel('Email')).toHaveValue(email);
  const authenticated = page.waitForResponse(response => response.url() === api + '/e2e/auth/session' && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  expect((await authenticated).ok()).toBe(true);
  await expect(page.getByRole('heading', { name: /your game night starts here/i })).toBeVisible();
}
async function start(page: Page) {
  await page.goto('/manage/steam');
  await page.getByRole('button', { name: /^(Link Steam account|Start again with Steam)$/ }).click();
  await expect(page.getByRole('heading', { name: 'Controlled Steam fixture' })).toBeVisible();
}
async function approve(page: Page) {
  await page.getByRole('button', { name: 'Approve synthetic Steam identity' }).click();
  await expect(page.getByRole('heading', { name: 'Steam Library', exact: true })).toBeVisible();
}
async function handle(page: Page): Promise<Handle | null> { return page.evaluate(key => JSON.parse(sessionStorage.getItem(key) ?? 'null'), key); }
async function call(page: Page, path: string, body?: unknown) {
  return page.evaluate(async ({ url, body }) => {
    const session = JSON.parse(localStorage.getItem('game-library-e2e-session')!);
    const response = await fetch(url, { method: body === undefined ? 'GET' : 'POST', credentials: url.includes('/steam/link/v2/') ? 'include' : 'omit',
      headers: { Authorization: 'Bearer ' + session.access_token, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body) });
    return { status: response.status, body: response.status === 204 ? null : await response.json() };
  }, { url: api + path, body });
}
async function expectUnlinked(page: Page) { expect((await call(page, '/steam/status')).body.linked).toBe(false); }

test.beforeEach(async ({ page, context, request }) => {
  // Deny even incidental external requests (e.g. provider favicons). The explicit
  // provider route installed later handles native Steam navigation in memory.
  await context.route('**/*', route => {
    const host = new URL(route.request().url()).hostname;
    return host === '127.0.0.1' || host === 'localhost' ? route.fallback() : route.abort();
  });
  expect((await request.post(api + '/e2e/reset')).ok()).toBe(true);
  await signIn(page, `steam-${randomUUID()}@example.test`);
});

test('native provider return stages only, full-ID review survives refresh, explicit confirm preserves import and unlink', async ({ page, context }) => {
  const provider = await fakeProvider(context);
  const targets: string[] = [];
  page.on('request', request => { if (request.url().startsWith(root)) targets.push(new URL(request.url()).pathname + new URL(request.url()).search); });
  await start(page); await approve(page);
  await expect(page.getByTestId('verified-steam-id')).toHaveText(provider.identity);
  await expect(page.getByText('Game Library account:', { exact: false })).toBeVisible();
  await expectUnlinked(page);
  const metadata = await handle(page);
  expect(Object.keys(metadata!).sort()).toEqual(['expiresAt', 'initiatingUserId', 'transactionId']);
  const cookie = (await context.cookies(api)).find(cookie => cookie.name === 'GameLibrary.SteamLink.v2')!;
  expect(!!cookie && cookie.httpOnly && !cookie.secure && cookie.sameSite === 'Lax' && cookie.path === '/' && cookie.domain === '127.0.0.1').toBe(true);
  expect(cookie.expires <= Date.parse(metadata!.expiresAt) / 1000).toBe(true);
  expect(new URL(page.url()).searchParams.has('steamLink')).toBe(false);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  await page.reload();
  const confirm = page.getByRole('button', { name: 'Confirm linking this Steam account' });
  await expect(confirm).toBeVisible(); await expectUnlinked(page);
  await confirm.evaluate(button => { (button as HTMLButtonElement).click(); (button as HTMLButtonElement).click(); });
  await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  expect(await handle(page)).toBeNull();
  expect((await context.cookies(api)).find(current => current.name === cookie.name)?.value === cookie.value).toBe(true);
  expect(targets.filter(path => path === '/steam/link/v2/confirm')).toHaveLength(1);
  expect(targets.filter(path => !path.includes('/callback')).every(path => !path.includes('?'))).toBe(true);
  const preview = page.waitForResponse(response => response.url() === api + '/steam/library/preview' && response.request().method() === 'POST');
  await page.getByRole('button', { name: 'Preview Steam library' }).click();
  expect((await preview).ok()).toBe(true);
  await expect(page.getByText('Dota 2', { exact: true })).toBeVisible();
  await page.locator('.steam-candidate').filter({ hasText: 'Dota 2' }).locator('input').check();
  await page.getByRole('button', { name: 'Import selected' }).click();
  await expect(page.getByText('Imported: 1')).toBeVisible();
  await page.getByRole('button', { name: 'Unlink', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'No Steam Account Linked' })).toBeVisible();
  await page.goto('/library'); await expect(page.getByText('Dota 2', { exact: true })).toBeVisible();
});

test('same-browser injected valid identity is only staged and can be declined; callback replay cannot replace it', async ({ page, context }) => {
  const provider = await fakeProvider(context);
  await start(page); await approve(page);
  const metadata = await handle(page);
  await expect(page.getByTestId('verified-steam-id')).toHaveText(provider.identity);
  await expectUnlinked(page);
  expect((await call(page, '/steam/link/v2/confirm', { transactionId: metadata!.transactionId, expectedSteamId64: '1' })).body.code).toBe('REVIEW_REQUIRED');
  const second = assertion(provider.authorization!, steamId());
  // Same initiating browser automatically carries B. This proves staging is not consent.
  await page.goto(second.toString());
  await expect(page.getByTestId('verified-steam-id')).toHaveText(provider.identity);
  await expectUnlinked(page);
  await page.getByRole('button', { name: 'Cancel linking', exact: true }).click();
  await expect(page.getByText('Linking cancelled.', { exact: false })).toBeVisible();
  expect(await handle(page)).toBeNull();
  expect((await call(page, '/steam/link/v2/confirm', { transactionId: metadata!.transactionId, expectedSteamId64: provider.identity })).status).toBe(409);
  await expectUnlinked(page);
});

test('provider cancellation is nonmutating; explicit cancellation and restart are distinct', async ({ page, context }) => {
  await fakeProvider(context); await start(page);
  await page.getByRole('link', { name: 'Decline at provider' }).click();
  await expect(page.getByText('Steam verification has not completed.', { exact: false })).toBeVisible();
  expect((await call(page, '/steam/link/v2/review', { transactionId: (await handle(page))!.transactionId })).body.state).toBe('Pending');
  await expectUnlinked(page);
  await page.getByRole('button', { name: 'Cancel linking', exact: true }).click();
  await expect(page.getByText('Linking cancelled.', { exact: false })).toBeVisible();
});

test('two real cookie contexts reject transferred URL, derived proof and authenticated receiver but sender completes', async ({ page, context, browser }) => {
  const email = `transfer-${randomUUID()}@example.test`; await signIn(page, email);
  const provider = await fakeProvider(context); await start(page);
  const receiver = await browser.newContext();
  try {
    const other = await receiver.newPage(); await signIn(other, email);
    // Replay the complete assertion, without B. No provider success can replace proof.
    await other.goto(provider.callback!);
    await expect(other.getByRole('heading', { name: 'No Steam Account Linked' })).toBeVisible();
    expect((await receiver.cookies(api)).some(c => c.name.includes('SteamLink'))).toBe(false);
    const state = new URL(provider.callback!).searchParams.get('state')!;
    const derived = createHash('sha256').update(state).digest('base64url');
    await receiver.addCookies([{ name: 'GameLibrary.SteamLink.v2', value: derived, url: api, httpOnly: true, sameSite: 'Lax' }]);
    await other.goto(provider.callback!); await expectUnlinked(other);
    await approve(page); await expect(page.getByTestId('verified-steam-id')).toHaveText(provider.identity);
    await expectUnlinked(page);
  } finally { await receiver.close(); }
});

test('cleared or refused proof cookie and missing tab storage fail closed without fallback or auto-confirm', async ({ page, context, playwright }) => {
  const provider = await fakeProvider(context); await start(page);
  await context.clearCookies();
  await approve(page);
  await expect(page.getByText('missing browser proof.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm linking this Steam account' })).toHaveCount(0);
  await expectUnlinked(page);
  // Deterministic issuance-refusal fixture. Do not claim this reproduces every
  // browser's third-party cookie policy: hosted cross-site evidence remains pending.
  const isolated = await playwright.request.newContext();
  await page.route(root + 'requests', async route => {
    const response = await isolated.post(root + 'requests', { headers: await route.request().allHeaders(), data: {} });
    const headers = response.headers(); delete headers['set-cookie'];
    await route.fulfill({ status: response.status(), headers, body: await response.body() });
  });
  try {
    await start(page); await approve(page);
    await expect(page.getByText('missing browser proof.', { exact: false })).toBeVisible();
    expect((await context.cookies(api)).some(cookie => cookie.name.includes('SteamLink'))).toBe(false);
  } finally { await isolated.dispose(); await page.unroute(root + 'requests'); }
  await page.evaluate(key => sessionStorage.removeItem(key), key);
  await page.goto('/manage/steam?steamLink=review&keep=yes');
  await expect(page.getByRole('heading', { name: 'No Steam Account Linked' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm linking this Steam account' })).toHaveCount(0);
  expect(new URL(page.url()).searchParams.get('keep')).toBe('yes');
  expect(provider.count).toBe(2);
});

test('browser expiry removes local identity and handle; equality never issues confirmation', async ({ page, context }) => {
  await page.clock.install(); await fakeProvider(context); await start(page); await approve(page);
  await expect(page.getByTestId('verified-steam-id')).toBeVisible();
  const metadata = await handle(page);
  await page.clock.fastForward(Math.max(0, Date.parse(metadata!.expiresAt) - Date.now()) + 1000);
  await expect(page.getByText('The linking request expired.', { exact: false })).toBeVisible();
  expect(await handle(page)).toBeNull();
  await expect(page.getByRole('button', { name: 'Confirm linking this Steam account' })).toHaveCount(0);
  await expectUnlinked(page);
});

test('wrong application account cannot review or confirm; switching back requires fresh review and explicit confirmation', async ({ page, context }) => {
  const email = `owner-${randomUUID()}@example.test`; await signIn(page, email);
  const provider = await fakeProvider(context); await start(page); await approve(page);
  const metadata = (await handle(page))!;
  const otherTab = await context.newPage(); await signIn(otherTab, 'other@example.com');
  await expect(page.getByTestId('verified-steam-id')).toHaveCount(0);
  expect(await handle(page)).toBeNull();
  expect((await call(page, '/steam/link/v2/review', { transactionId: metadata.transactionId })).status).toBe(404);
  expect((await call(page, '/steam/link/v2/confirm', { transactionId: metadata.transactionId, expectedSteamId64: provider.identity })).status).toBe(404);
  await expectUnlinked(page);
  await signIn(otherTab, email);
  // A maliciously restored locator is not authority; returning U still needs fresh authorized review.
  await page.evaluate(({ key, metadata }) => sessionStorage.setItem(key, JSON.stringify(metadata)), { key, metadata });
  await page.reload(); await expect(page.getByTestId('verified-steam-id')).toHaveText(provider.identity);
  await expectUnlinked(page);
  await page.getByRole('button', { name: 'Confirm linking this Steam account' }).click();
  await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  await otherTab.close();
});

test('lost committed confirmation recovers through durable review after refresh, with no automatic replay', async ({ page, context }) => {
  await fakeProvider(context); await start(page); await approve(page);
  let confirmations = 0;
  await page.route(root + 'confirm', async route => {
    confirmations++; const response = await route.fetch(); expect(response.status()).toBe(200);
    await route.abort('failed'); // COMMIT happened; only the acknowledgment is lost.
  });
  await page.getByRole('button', { name: 'Confirm linking this Steam account' }).click();
  await expect(page.getByText('Confirmation outcome is unknown.', { exact: false })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Confirm linking this Steam account' })).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  await expect(page.getByText('Steam account linked.', { exact: true })).toBeVisible();
  expect(await handle(page)).toBeNull(); expect(confirmations).toBe(1);
});

test('parallel direct confirmations give one current receipt and callback/receipt replays cannot relink after unlink', async ({ page, context }) => {
  const provider = await fakeProvider(context); await start(page); await approve(page);
  const metadata = (await handle(page))!;
  const body = { transactionId: metadata.transactionId, expectedSteamId64: provider.identity };
  const result = await Promise.all([call(page, '/steam/link/v2/confirm', body), call(page, '/steam/link/v2/confirm', body)]);
  expect(result.map(r => r.status)).toEqual([200, 200]);
  await page.reload(); await expect(page.getByRole('heading', { name: 'Linked Steam Account', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Unlink', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'No Steam Account Linked' })).toBeVisible();
  expect((await call(page, '/steam/link/v2/confirm', body)).body.code).toBe('LINK_NOT_CURRENT');
  await page.goto(provider.callback!); await expectUnlinked(page);
});

test('ordinary unavailable/old-client status and forged success flags expose no linking UI or legacy fallback', async ({ page }) => {
  let mutations = 0;
  page.on('request', request => { if (request.url().startsWith(root) && request.method() === 'POST') mutations++; });
  await page.route(api + '/steam/status', route => route.fulfill({ json: { enabled: true, linked: false, linkAvailable: true, importAvailable: true } }));
  await page.goto('/manage/steam?steamLink=success');
  await expect(page.getByText('Steam linking is temporarily unavailable.', { exact: false }).first()).toBeVisible();
  await expect(page.getByRole('button', { name: 'Link Steam account', exact: true })).toHaveCount(0);
  expect(mutations).toBe(0);
});

test('storage rejection and a malicious authorization response cannot navigate outside the trusted provider', async ({ page }) => {
  await page.goto('/manage/steam');
  // Install after native page load (full navigation creates a new JS realm).
  await page.evaluate(key => {
    const original = Storage.prototype.setItem;
    Storage.prototype.setItem = function(name, value) {
      if (name === key) throw new DOMException('Storage rejected', 'SecurityError');
      return original.call(this, name, value);
    };
  }, key);
  await page.getByRole('button', { name: 'Link Steam account', exact: true }).click();
  await expect(page.getByText('Unable to continue securely.', { exact: false })).toBeVisible();
  expect(new URL(page.url()).origin).toBe('http://127.0.0.1:4200');
  await page.reload();
  await page.route(root + 'requests', async route => {
    const response = await route.fetch();
    await route.fulfill({ response, json: { ...await response.json(), authorizationUrl: 'https://evil.example/collect' } });
  });
  await page.getByRole('button', { name: 'Link Steam account', exact: true }).click();
  await expect(page.getByText('Steam destination could not be verified.', { exact: false })).toBeVisible();
  expect(new URL(page.url()).origin).toBe('http://127.0.0.1:4200');
  expect(await handle(page)).toBeNull(); await expectUnlinked(page);
});

test('account switch while creation or review response waits suppresses stale navigation and private identity', async ({ page, context }) => {
  await fakeProvider(context);
  const originalEmail = `stale-${randomUUID()}@example.test`;
  await signIn(page, originalEmail);
  const other = await context.newPage();
  let release!: () => void;
  let arrived!: () => void;
  let waiting = new Promise<void>(resolve => { arrived = resolve; });
  let gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(root + 'requests', async route => {
    const response = await route.fetch(); arrived(); await gate; await route.fulfill({ response });
  });
  await page.goto('/manage/steam'); await page.getByRole('button', { name: 'Link Steam account', exact: true }).click();
  try { await waiting; await signIn(other, 'other@example.com'); }
  finally { release(); }
  await expect(page.getByText('Account changed; start again.', { exact: true })).toBeVisible();
  expect(new URL(page.url()).origin).toBe('http://127.0.0.1:4200'); expect(await handle(page)).toBeNull();
  await page.unroute(root + 'requests');
  await signIn(other, originalEmail);
  await start(page);
  waiting = new Promise<void>(resolve => { arrived = resolve; });
  gate = new Promise<void>(resolve => { release = resolve; });
  await page.route(root + 'review', async route => {
    const response = await route.fetch(); arrived(); await gate; await route.fulfill({ response });
  });
  await page.getByRole('button', { name: 'Approve synthetic Steam identity' }).click();
  try { await waiting; await signIn(other, 'other@example.com'); }
  finally { release(); }
  await expect(page.getByText('Account changed; start again.', { exact: true })).toBeVisible();
  await expect(page.getByTestId('verified-steam-id')).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'Confirm linking this Steam account' })).toHaveCount(0);
  await expectUnlinked(page); await other.close();
});

test('review and SPA fallback deliver framing headers and foreign-origin iframe cannot render consent', async ({ page, request }) => {
  for (const path of ['/manage/steam', '/index.html', '/unmatched-spa-fallback']) {
    const response = await request.get('http://127.0.0.1:4200' + path);
    expect(response.headers()['content-security-policy']).toBe("frame-ancestors 'none'");
    expect(response.headers()['x-frame-options']).toBe('DENY');
  }
  await page.route(api + '/allowed-frame', route => route.fulfill({ contentType: 'text/html', body: '<h1>Frame control rendered</h1>' }));
  await page.route(api + '/foreign-frame', route => route.fulfill({ contentType: 'text/html', body: '<iframe title="control" src="/allowed-frame"></iframe><iframe title="hostile" src="http://127.0.0.1:4200/manage/steam"></iframe>' }));
  await page.goto(api + '/foreign-frame');
  await expect(page.frameLocator('iframe[title="control"]').getByRole('heading', { name: 'Frame control rendered' })).toBeVisible();
  await expect.poll(() => page.frames().some(frame => frame.url().startsWith('chrome-error://'))).toBe(true);
  await expect(page.frameLocator('iframe[title="hostile"]').getByRole('heading', { name: 'Steam Library' })).toHaveCount(0);
});

test('latest committed tab wins; reversed Set-Cookie delivery safely forces both tabs to restart', async ({ page, context, playwright }) => {
  await fakeProvider(context);
  const isolated = await playwright.request.newContext();
  let release!: () => void;
  const delayed = new Promise<void>(resolve => { release = resolve; });
  let committed!: () => void;
  const firstCommitted = new Promise<void>(resolve => { committed = resolve; });
  await page.route(root + 'requests', async route => {
    const response = await isolated.post(root + 'requests', { headers: await route.request().allHeaders(), data: {} });
    expect(response.status()).toBe(201); committed();
    await delayed;
    await route.fulfill({ status: response.status(), headers: response.headers(), body: await response.body() });
  });
  const other = await context.newPage();
  try {
    await page.goto('/manage/steam'); await page.getByRole('button', { name: 'Link Steam account', exact: true }).click();
    await firstCommitted;
    await start(other); // SQL replaces first while first browser response is held.
    release(); await expect(page.getByRole('heading', { name: 'Controlled Steam fixture' })).toBeVisible();
    await page.getByRole('button', { name: 'Approve synthetic Steam identity' }).click();
    await expect(page.getByText('missing browser proof.', { exact: false })).toBeVisible();
    await other.getByRole('button', { name: 'Approve synthetic Steam identity' }).click();
    await expect(other.getByText('missing browser proof.', { exact: false })).toBeVisible();
    await expectUnlinked(page); await expectUnlinked(other);
  } finally { release(); await isolated.dispose(); await other.close(); }
});
