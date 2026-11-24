import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { apiTokenInterceptor } from '../../../core/auth/token.interceptor';
import { environment } from '../../../../environments/environment';
import { SteamLinkBrowser, SteamLinkHandle, steamLinkStorageKey, trustedSteamUrl, validLinkHandle } from './steam-link-browser';
import { SteamLinkFlow } from './steam-link-flow';
import { SteamPage } from './steam-page';

const root = `${environment.apiBaseUrl}/steam/link/v2/`;
const session = createTestSession();
const id = '11111111-1111-1111-1111-111111111111';
const steamId = '76561198000000000';
const url = 'https://steamcommunity.com/openid/login?openid.return_to=synthetic';

class BrowserFixture {
  stored: SteamLinkHandle | null = null;
  read = vi.fn(() => this.stored);
  save = vi.fn((handle: SteamLinkHandle) => { this.stored = { ...handle }; });
  clear = vi.fn(() => { this.stored = null; });
  navigate = vi.fn();
}

describe('Steam v2 trusted tab flow', () => {
  let auth: AuthService;
  let mock: ReturnType<typeof createMockSupabase>;
  let browser: BrowserFixture;
  let flow: SteamLinkFlow;
  let http: HttpTestingController;
  const expires = () => new Date(Date.now() + 600_000).toISOString();
  const handle = (): SteamLinkHandle => ({ transactionId: id, initiatingUserId: session.user.id, expiresAt: expires() });

  beforeEach(async () => {
    mock = createMockSupabase(session); browser = new BrowserFixture();
    TestBed.configureTestingModule({ providers: [provideHttpClient(withInterceptors([apiTokenInterceptor])), provideHttpClientTesting(), provideRouter([]),
      { provide: SUPABASE_CLIENT, useValue: mock.client }, { provide: SteamLinkBrowser, useValue: browser }] });
    auth = TestBed.inject(AuthService); await auth.initialize();
    flow = TestBed.inject(SteamLinkFlow); await Promise.resolve();
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => { http.verify(); vi.useRealTimers(); });

  function restore() { browser.stored = handle(); flow.restore(); }
  function stage() {
    restore(); flow.refresh();
    http.expectOne(root + 'review').flush({ state: 'AwaitingConfirmation', steamId64: steamId, expiresAt: flow.handle()!.expiresAt });
  }

  it('stores only T/U/deadline before native navigation and blocks repeated creation', () => {
    flow.start(); flow.start();
    const request = http.expectOne(root + 'requests');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    request.flush({ protocolVersion: 2, transactionId: id, authorizationUrl: url, expiresAt: expires() });
    expect(Object.keys(browser.stored!).sort()).toEqual(['expiresAt', 'initiatingUserId', 'transactionId']);
    expect(JSON.stringify(browser.stored)).not.toContain('openid');
    expect(browser.navigate).toHaveBeenCalledExactlyOnceWith(url);
    expect(browser.save.mock.invocationCallOrder[0]).toBeLessThan(browser.navigate.mock.invocationCallOrder[0]);
    flow.start(); http.expectNone(root + 'requests');
  });

  it.each([
    'http://steamcommunity.com/openid/login?x=y', 'https://evil.example/openid/login?x=y',
    'https://steamcommunity.com.evil.example/openid/login?x=y', 'https://user@steamcommunity.com/openid/login?x=y',
    'https://steamcommunity.com:444/openid/login?x=y', 'https://steamcommunity.com:443/openid/login?x=y',
    'https://steamcommunity.com/openid/login?x=y#fragment', 'https://steamcommunity.com/openid/login/?x=y',
    'https://steamcommunity.com/openid/login?x=y#',
    'javascript:alert(1)', 'https://steamcommunity.com/openid/../openid/login?x=y',
    'https://steamcommunity.com\\@evil.example/openid/login?x=y', '/steam/link/start/synthetic',
  ])('rejects off-contract destination without retaining it (%#)', (malicious) => {
    expect(trustedSteamUrl(malicious)).toBe(false);
    flow.start(); http.expectOne(root + 'requests').flush({ protocolVersion: 2, transactionId: id, authorizationUrl: malicious, expiresAt: expires() });
    expect(browser.navigate).not.toHaveBeenCalled(); expect(flow.handle()).toBeNull();
    expect(flow.message()).not.toContain(malicious);
  });

  it('fails closed if storage is blocked, with no native navigation or URL recovery', () => {
    browser.save.mockImplementation(() => { throw new Error('synthetic-private-storage-failure'); });
    flow.start(); http.expectOne(root + 'requests').flush({ protocolVersion: 2, transactionId: id, authorizationUrl: url, expiresAt: expires() });
    expect(browser.navigate).not.toHaveBeenCalled(); expect(flow.handle()).toBeNull(); expect(flow.message()).toContain('tab storage');
  });

  it.each(['create', 'review', 'confirm'])('ignores stale %s after rapid U→W→U and drops metadata even outside the page', (operation) => {
    if (operation === 'create') flow.start();
    if (operation === 'review') { restore(); flow.refresh(); }
    if (operation === 'confirm') { stage(); flow.confirm(); }
    const request = http.expectOne(root + (operation === 'create' ? 'requests' : operation));
    const generation = auth.generation();
    mock.emit('SIGNED_IN', createTestSession({ user: { ...session.user, id: 'user-2' } }));
    mock.emit('SIGNED_IN', session);
    expect(auth.generation()).toBe(generation + 2);
    request.flush(operation === 'create' ? { protocolVersion: 2, transactionId: id, authorizationUrl: url, expiresAt: expires() }
      : operation === 'review' ? { state: 'AwaitingConfirmation', steamId64: steamId, expiresAt: expires() }
        : { state: 'Linked', steamId64: steamId, linkedAt: new Date().toISOString() });
    expect(flow.review()).toBeNull(); expect(flow.handle()).toBeNull(); expect(browser.stored).toBeNull();
    expect(browser.navigate).not.toHaveBeenCalled(); expect(flow.message()).toContain('Account changed');
  });

  it('does not invalidate a same-subject refresh and sends the latest JWT at confirmation', () => {
    stage(); const generation = auth.generation();
    mock.emit('TOKEN_REFRESHED', createTestSession({ access_token: 'refreshed-access-token' }));
    expect(auth.generation()).toBe(generation); expect(flow.review()?.steamId64).toBe(steamId);
    flow.confirm(); flow.confirm();
    const confirm = http.expectOne(root + 'confirm');
    expect(confirm.request.headers.get('Authorization')).toBe('Bearer refreshed-access-token');
    expect(confirm.request.body).toEqual({ transactionId: id, expectedSteamId64: steamId });
    confirm.flush({ state: 'Linked', steamId64: steamId, linkedAt: new Date().toISOString() });
    expect(flow.handle()).toBeNull(); expect(browser.stored).toBeNull();
    http.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
    http.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it('lost confirmation requires durable review and another explicit choice, never an automatic retry', () => {
    stage(); flow.confirm();
    http.expectOne(root + 'confirm').flush({ code: 'LINK_OUTCOME_UNKNOWN', detail: 'untrusted-error-canary' }, { status: 503, statusText: 'Unavailable' });
    flow.confirm(); http.expectNone(root + 'confirm');
    expect(flow.review()).toBeNull(); expect(flow.handle()).not.toBeNull(); expect(flow.message()).not.toContain('canary');
    flow.refresh(); http.expectOne(root + 'review').flush({ state: 'Linked', steamId64: steamId, expiresAt: flow.handle()!.expiresAt });
    expect(flow.message()).toBe('Steam account linked.'); expect(flow.handle()).toBeNull(); http.expectNone(root + 'confirm');
  });

  it.each([401, 404, 409, 429, 500, 503, 0])('safe %i recovery hides identity and never retries mutations', (status) => {
    stage(); flow.confirm();
    http.expectOne(root + 'confirm').flush({ code: status === 409 ? 'LINK_CONFLICT' : 'untrusted', detail: 'private-error-canary' }, { status, statusText: 'Synthetic' });
    expect(flow.review()).toBeNull(); expect(flow.busy()).toBe(false); expect(flow.message()).not.toContain('canary');
    flow.confirm(); http.expectNone(root + 'confirm');
    if (status === 404) expect(flow.handle()).toBeNull();
  });

  it('clears on observed logout and refuses stale/current no-session responses without asserting revocation', () => {
    stage(); flow.confirm(); const request = http.expectOne(root + 'confirm');
    auth.clearLocalSession(); request.flush({ state: 'Linked', steamId64: steamId });
    expect(flow.handle()).toBeNull(); expect(flow.review()).toBeNull(); expect(browser.stored).toBeNull();
    flow.start(); http.expectNone(root + 'requests');
  });

  it('expires metadata/identity locally at equality and does not submit confirmation', () => {
    vi.useFakeTimers(); stage();
    vi.advanceTimersByTime(600_000);
    flow.confirm(); expect(flow.handle()).toBeNull(); expect(browser.stored).toBeNull(); expect(flow.message()).toContain('expired');
    http.expectNone(root + 'confirm');
  });

  it('requires matching tab owner and cannot discover a transaction from missing storage', () => {
    browser.stored = { ...handle(), initiatingUserId: 'other' }; flow.restore(); flow.refresh();
    expect(flow.message()).toContain('Account changed'); expect(flow.handle()).toBeNull(); http.expectNone(root + 'review');
  });

  it('explicit cancellation is single-submission, clears metadata and never invokes unlink', () => {
    stage(); flow.cancel(); flow.cancel(); flow.confirm();
    http.expectOne(root + 'cancel').flush(null);
    expect(flow.handle()).toBeNull(); expect(browser.stored).toBeNull(); expect(flow.message()).toContain('cancelled');
    http.expectNone(`${environment.apiBaseUrl}/steam/link`); http.expectNone(root + 'confirm');
  });

  it('restores only metadata on reload; an old presentation cannot become consent', () => {
    stage(); flow.suspend(); flow.restore(); flow.confirm();
    expect(flow.review()).toBeNull(); http.expectNone(root + 'confirm');
    flow.refresh(); http.expectOne(root + 'review').flush({ state: 'AwaitingConfirmation', steamId64: steamId, expiresAt: flow.handle()!.expiresAt });
    expect(flow.review()?.steamId64).toBe(steamId); http.expectNone(root + 'confirm');
  });

  it('full SteamID and current application account are escaped; forged flags cannot auto-confirm', async () => {
    browser.stored = handle();
    const fixture = TestBed.createComponent(SteamPage); fixture.detectChanges();
    http.expectOne(`${environment.apiBaseUrl}/steam/status`).flush({ enabled: true, linkAvailable: true, linkProtocolVersion: 2, linked: false, importAvailable: true });
    await Promise.resolve();
    http.expectOne(root + 'review').flush({ state: 'AwaitingConfirmation', steamId64: steamId, expiresAt: browser.stored!.expiresAt });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(steamId);
    expect(fixture.nativeElement.textContent).toContain('Game Library account:');
    expect(fixture.nativeElement.textContent).toContain(session.user.email);
    expect(fixture.nativeElement.textContent).toContain('Confirm linking this Steam account');
    http.expectNone(root + 'confirm');
    mock.emit('SIGNED_IN', createTestSession({ user: { ...session.user, id: 'other', email: '<img src=x onerror=alert(1)>' } }));
    http.expectOne(`${environment.apiBaseUrl}/steam/status`).flush({ enabled: true, linkAvailable: false, linked: false });
    fixture.detectChanges(); expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain(steamId);
  });

  it.each(['preview', 'import', 'unlink', 'status'])('stale %s response cannot restore a prior subject private state', (operation) => {
    const fixture = TestBed.createComponent(SteamPage); fixture.detectChanges();
    const initial = http.expectOne(`${environment.apiBaseUrl}/steam/status`);
    if (operation !== 'status') {
      initial.flush({ enabled: true, linked: true, linkAvailable: false, importAvailable: true, steamId64: steamId });
      if (operation === 'preview') fixture.componentInstance['onPreview']();
      if (operation === 'unlink') fixture.componentInstance['onUnlink']();
      if (operation === 'import') {
        fixture.componentInstance['candidates'].set([{ steamAppId: 1, name: 'private-game', alreadyImported: false, playtimeForeverMinutes: null }]);
        fixture.componentInstance['selected'].set(new Set([1])); fixture.componentInstance['onImport']();
      }
    }
    const request = operation === 'status' ? initial : http.expectOne(`${environment.apiBaseUrl}/steam/${operation === 'unlink' ? 'link' : 'library/' + operation}`);
    auth.clearLocalSession();
    request.flush(operation === 'preview' ? { state: 'Success', candidates: [{ name: 'old-private-canary' }], message: null }
      : operation === 'import' ? { imported: [{ name: 'old-private-canary' }], alreadyImported: [], unavailable: [], invalid: [] }
        : operation === 'unlink' ? null : { linked: true, steamId64: steamId });
    expect(fixture.componentInstance['status']()).toBeNull();
    expect(fixture.componentInstance['candidates']()).toEqual([]);
    expect(fixture.componentInstance['importResult']()).toBeNull();
    fixture.detectChanges(); expect(fixture.nativeElement.textContent).not.toContain('old-private-canary');
    http.expectNone(`${environment.apiBaseUrl}/steam/status`);
  });

  it('visibly disables unlink while durable receipt review is pending, not merely in its handler', async () => {
    browser.stored = handle();
    const fixture = TestBed.createComponent(SteamPage); fixture.detectChanges();
    http.expectOne(`${environment.apiBaseUrl}/steam/status`).flush({ enabled: true, linked: true, linkAvailable: true, linkProtocolVersion: 2, importAvailable: true });
    await Promise.resolve(); fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.btn--danger') as HTMLButtonElement).disabled).toBe(true);
    fixture.componentInstance['onUnlink'](); http.expectNone(`${environment.apiBaseUrl}/steam/link`);
    http.expectOne(root + 'review').flush({ code: 'LINK_OUTCOME_UNKNOWN' }, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges(); expect((fixture.nativeElement.querySelector('.btn--danger') as HTMLButtonElement).disabled).toBe(false);
  });
});

describe('Steam tab storage schema', () => {
  it('rejects extra secret fields and invalid locators/deadlines', () => {
    const handle = { transactionId: id, initiatingUserId: 'u', expiresAt: new Date().toISOString() };
    expect(validLinkHandle(handle)).toBe(true);
    for (const value of [{ ...handle, authorizationUrl: url }, { ...handle, state: 'secret' }, { ...handle, transactionId: 'not-uuid' }, { ...handle, expiresAt: 'bad' }])
      expect(validLinkHandle(value)).toBe(false);
  });
  it('writes exactly the approved triple, round trips it, and removes malformed storage', () => {
    TestBed.configureTestingModule({}); const browser = TestBed.inject(SteamLinkBrowser);
    const handle = { transactionId: id, initiatingUserId: 'u', expiresAt: new Date().toISOString() };
    browser.save(handle); expect(JSON.parse(sessionStorage.getItem(steamLinkStorageKey)!)).toEqual(handle);
    expect(browser.read()).toEqual(handle);
    sessionStorage.setItem(steamLinkStorageKey, '{'); expect(browser.read()).toBeNull();
    expect(sessionStorage.getItem(steamLinkStorageKey)).toBeNull();
  });
});
