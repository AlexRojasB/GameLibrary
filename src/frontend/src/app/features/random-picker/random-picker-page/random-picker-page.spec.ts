import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { routes } from '../../../app.routes';
import { AuthService } from '../../../core/auth/auth.service';
import { authGuard } from '../../../core/auth/auth.guard';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { PickerRound, RandomPickerFilters, RandomPickerItem, RandomPickerMode } from '../random-picker';
import { RandomPickerPage } from './random-picker-page';

const video: RandomPickerItem = {
  libraryEntryId: 'entry-1', gameId: 'game-1', gameType: 'VideoGame', name: 'Hades',
  coverImageUrl: null, coverUploadId: null, isFamilyFriendly: false, rating: 5, notes: 'Fast runs',
  platformIds: ['p1'], genreIds: ['g1'], gameStatus: 'Playing', progressPercentage: 50,
  minimumPlayers: 1, maximumPlayers: 1, approximateDuration: null, interactionType: null,
};
const board: RandomPickerItem = {
  ...video, libraryEntryId: 'entry-2', gameId: 'game-2', gameType: 'BoardGame', name: 'Pandemic',
  isFamilyFriendly: true, platformIds: [], genreIds: [], gameStatus: null, progressPercentage: null,
  minimumPlayers: 2, maximumPlayers: 4, approximateDuration: 45, interactionType: 'Cooperative',
};
const filters: RandomPickerFilters = {
  platformIds: [], genreIds: [], gameStatuses: [], playerCount: null, availableDuration: null,
  interactionTypes: [], ratingMin: null, familyFriendlyOnly: false,
};
function round(mode: RandomPickerMode = 'All', patch: Partial<PickerRound> = {}): PickerRound {
  return { mode, revision: 0, filters: { ...filters }, state: null, result: null,
    history: [], shownLibraryEntryIds: [], proposalUnavailable: false, ...patch };
}
function proposal(mode: RandomPickerMode = 'All', items: RandomPickerItem[] = [video], revision = 1): PickerRound {
  return round(mode, { revision, result: items[0], state: 'SUCCESS', history: items,
    shownLibraryEntryIds: items.map(i => i.libraryEntryId).reverse() });
}
@Component({ template: '' })
class DestinationStub {}

describe('RandomPickerPage current-round recovery', () => {
  let fixture: ComponentFixture<RandomPickerPage>;
  let http: HttpTestingController;
  let mock: ReturnType<typeof createMockSupabase>;

  beforeEach(async () => {
    mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({
      imports: [RandomPickerPage],
      providers: [provideHttpClient(), provideHttpClientTesting(),
        provideRouter(['login', 'video-games', 'board-games'].map(path => ({ path, component: DestinationStub }))),
        { provide: SUPABASE_CLIENT, useValue: mock.client }],
    });
    await TestBed.inject(AuthService).initialize();
    http = TestBed.inject(HttpTestingController);
    createPage();
  });
  afterEach(() => http.verify());

  function createPage() {
    fixture = TestBed.createComponent(RandomPickerPage);
    fixture.detectChanges();
    http.expectOne(r => r.url.endsWith('/platforms')).flush([{ id: 'p1', name: 'Steam' }]);
    http.expectOne(r => r.url.endsWith('/genres')).flush([{ id: 'g1', name: 'Action' }]);
  }
  function load(value = round()) {
    http.expectOne(r => r.method === 'GET' && r.url.endsWith(`/rounds/${value.mode}`)).flush(value);
    fixture.detectChanges();
  }
  function page() { return fixture.componentInstance; }
  function text() { return (fixture.nativeElement as HTMLElement).textContent ?? ''; }
  function click(label: string) {
    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find(b => b.textContent?.trim() === label);
    if (!button) throw new Error(`Missing button: ${label}`);
    button.click();
    fixture.detectChanges();
  }
  function pick(): TestRequest { return http.expectOne(r => r.url.endsWith('/random-picker/pick')); }
  function acknowledge(request: TestRequest, value: PickerRound) {
    request.flush({ state: value.state, result: value.result, round: value });
    fixture.detectChanges();
  }
  function propose(item = video) {
    load();
    click('Pick a game');
    acknowledge(pick(), proposal('All', [item]));
  }
  function mode(value: RandomPickerMode) {
    page()['onMode'](value);
    fixture.detectChanges();
  }
  function save(value: PickerRound): TestRequest {
    const request = http.expectOne(r => r.method === 'PUT' && r.url.endsWith(`/rounds/${value.mode}`));
    request.flush(value);
    fixture.detectChanges();
    return request;
  }

  it('uses the auth guard and defaults to All with actions disabled until recovery', () => {
    expect(routes.find(r => r.path === 'random-picker')?.canActivate).toEqual([authGuard]);
    expect(routes.find(r => r.path === 'random-picker')?.loadComponent).toBeTypeOf('function');
    expect(routes.find(r => r.path === 'random-picker')?.component).toBeUndefined();
    expect(page()['mode']()).toBe('All');
    expect(page()['ready']()).toBe(false);
    load();
    expect(page()['ready']()).toBe(true);
    expect(text()).toContain('No picker filters active.');
  });

  it.each<RandomPickerMode>(['All', 'VideoGames', 'BoardGames'])('recovers %s proposal, ordered history, shown IDs and its filters', value => {
    load();
    if (value !== 'All') mode(value);
    else page()['reloadRound']();
    const item = value === 'BoardGames' ? board : video;
    const prior = { ...item, gameId: 'old', libraryEntryId: 'old-entry', name: 'Older pick' };
    const context = { ...filters, playerCount: 2, familyFriendlyOnly: true };
    load({ ...proposal(value, [item, prior], 7), filters: context });
    expect(page()['currentResult']()).toEqual(item);
    expect(page()['visibleHistory']()).toEqual([item, prior]);
    expect(page()['shownLibraryEntryIds']()).toEqual(['old-entry', item.libraryEntryId]);
    expect(page()['filters']()).toEqual(context);
    expect(text()).toContain('2 picker filters active.');
    const history = (fixture.nativeElement as HTMLElement).querySelector('.random-picker-card--history');
    expect(history?.textContent).toContain('Older pick');
    expect(history?.textContent).not.toContain('Select Game');
    expect(history?.textContent).not.toContain('Log play');
  });

  it('Another uses acknowledged revisions, has no client history authority, and keeps newest first', () => {
    propose();
    expect(text()).toContain('Steam');
    expect(text()).toContain('Action');
    expect(text()).toContain('Notes: Fast runs');
    click('Another');
    const request = pick();
    expect(request.request.body).toMatchObject({ mode: 'All', roundRevision: 1 });
    expect(request.request.body.operationId).toMatch(/^[0-9a-f-]{36}$/);
    expect(request.request.body).not.toHaveProperty('history');
    expect(request.request.body).not.toHaveProperty('shownLibraryEntryIds');
    acknowledge(request, proposal('All', [board, video], 2));
    expect(page()['visibleHistory']()).toEqual([board, video]);
    expect(text()).toContain('45 min');
    expect(text()).toContain('2-4 players');
    expect(text()).not.toContain('Skip');
    http.expectNone(r => r.url.endsWith('/play-log'));
  });

  it('switches between independent rounds without carrying filters or history across modes', () => {
    load({ ...proposal(), filters: { ...filters, ratingMin: 4 } });
    mode('BoardGames');
    expect(page()['visibleHistory']()).toEqual([]);
    load({ ...proposal('BoardGames', [board], 5), filters: { ...filters, availableDuration: 60 } });
    expect(text()).toContain('Available duration');
    expect(text()).not.toContain('Minimum rating');
    mode('VideoGames');
    load(round('VideoGames'));
    expect(page()['filters']().availableDuration).toBeNull();
    expect(page()['visibleHistory']()).toEqual([]);
    mode('All');
    load({ ...proposal(), filters: { ...filters, ratingMin: 4 } });
    expect(page()['currentResult']()).toEqual(video);
    expect(page()['filters']().ratingMin).toBe(4);
  });

  it('filter edits preserve history/shown IDs, clear proposal, serialize writes and recover the latest context', () => {
    propose();
    page()['onPlayerCount']('2');
    const first = http.expectOne(r => r.method === 'PUT' && r.url.endsWith('/rounds/All'));
    expect(first.request.body).toMatchObject({ playerCount: 2, roundRevision: 1 });
    page()['onFamilyFriendlyOnly'](true);
    http.expectNone(r => r.method === 'PUT');
    expect(page()['currentResult']()).toBeNull();
    expect(page()['visibleHistory']()).toEqual([video]);
    first.flush({ ...proposal(), revision: 2, result: null, state: null, filters: { ...filters, playerCount: 2 } });
    expect(page()['filters']().familyFriendlyOnly).toBe(true); // older ACK cannot revert newer edits
    const latest = save({ ...proposal(), revision: 3, result: null, state: null,
      filters: { ...filters, playerCount: 2, familyFriendlyOnly: true } });
    expect(latest.request.body).toMatchObject({ roundRevision: 2, playerCount: 2, familyFriendlyOnly: true });
    expect(page()['shownLibraryEntryIds']()).toEqual(['entry-1']);
    click('Clear filters');
    save({ ...proposal(), revision: 4, result: null, state: null });
    expect(page()['visibleHistory']()).toEqual([video]);
    expect(page()['filters']()).toEqual(filters);
  });

  it('recreates native filter controls between rounds even if an edit and mode switch share one render', () => {
    load();
    const checkbox = (fixture.nativeElement as HTMLElement).querySelector('.random-picker__family-filter input') as HTMLInputElement;
    checkbox.click();
    expect(checkbox.checked).toBe(true);
    const pending = http.expectOne(r => r.method === 'PUT');
    // No intermediate detectChanges: the native control is dirty before Angular
    // has rendered the edited value. The next mode must not inherit that DOM state.
    page()['onMode']('VideoGames');
    load(round('VideoGames'));
    const recovered = (fixture.nativeElement as HTMLElement).querySelector('.random-picker__family-filter input') as HTMLInputElement;
    expect(recovered).not.toBe(checkbox);
    expect(recovered.checked).toBe(false);
    expect(page()['filters']().familyFriendlyOnly).toBe(false);
    pending.flush(round('All', { revision: 1, filters: { ...filters, familyFriendlyOnly: true } }));
    expect(page()['mode']()).toBe('VideoGames');
    expect(page()['filters']().familyFriendlyOnly).toBe(false);
  });

  it.each<RandomPickerMode>(['All', 'VideoGames', 'BoardGames'])('Start over resets only active %s and keeps its filters', value => {
    load();
    if (value !== 'All') mode(value);
    else page()['reloadRound']();
    load({ ...proposal(value, [value === 'BoardGames' ? board : video]), filters: { ...filters, playerCount: 2 } });
    click('Start over');
    const reset = http.expectOne(r => r.url.endsWith(`/rounds/${value}/reset`));
    expect(reset.request.body).toMatchObject({ roundRevision: 1 });
    reset.flush(round(value, { revision: 2, filters: { ...filters, playerCount: 2 } }));
    fixture.detectChanges();
    expect(page()['visibleHistory']()).toEqual([]);
    expect(page()['shownLibraryEntryIds']()).toEqual([]);
    expect(page()['filters']().playerCount).toBe(2);
    http.expectNone(r => r.method !== 'GET');
  });

  it.each(['NO_CANDIDATES', 'ALL_ALREADY_SHOWN'] as const)('recovers %s without erasing history', state => {
    load({ ...proposal(), result: null, state });
    expect(text()).toContain(state === 'NO_CANDIDATES' ? 'No owned games match' : 'All matching games already shown');
    expect(page()['visibleHistory']()).toEqual([video]);
  });

  it('does not promote read-only history when the saved proposal is no longer eligible', () => {
    load({ ...proposal(), result: null, state: null, proposalUnavailable: true });
    expect(text()).toContain('saved proposal is no longer eligible');
    expect(text()).not.toContain('Select Game');
    expect(page()['currentResult']()).toBeNull();
    expect(page()['visibleHistory']()).toEqual([video]);
  });

  it('lets admitted writes finish across navigation and loads only after their acknowledgement', () => {
    propose();
    click('Another');
    const pending = pick();
    fixture.destroy();
    expect(pending.cancelled).toBe(false);
    createPage();
    http.expectNone(r => r.url.endsWith('/rounds/All'));
    acknowledge(pending, proposal('All', [board, video], 2));
    load(proposal('All', [board, video], 2));
    expect(page()['currentResult']()).toEqual(board);
    expect(page()['visibleHistory']()).toEqual([board, video]);
  });

  it('serializes filter changes behind pending picks and retains the committed shown history', () => {
    propose();
    click('Another');
    const pending = pick();
    page()['onPlayerCount']('3');
    expect(page()['currentResult']()).toBeNull();
    http.expectNone(r => r.method === 'PUT');
    acknowledge(pending, proposal('All', [board, video], 2));
    expect(page()['currentResult']()).toBeNull();
    save({ ...proposal('All', [board, video], 3), result: null, state: null, filters: { ...filters, playerCount: 3 } });
    expect(page()['visibleHistory']()).toEqual([board, video]);
  });

  it.each([0, 400, 409, 503])('discards unsent work after write failure %s and requires explicit reload, never replay', status => {
    propose();
    page()['onPlayerCount']('2');
    const pending = http.expectOne(r => r.method === 'PUT');
    page()['onPlayerCount']('3');
    pending.flush({ detail: 'Rejected' }, { status, statusText: 'Failure' });
    fixture.detectChanges();
    expect(page()['ready']()).toBe(false);
    expect(text()).toContain('Reload round');
    http.expectNone(r => r.method === 'PUT');
    page()['pick']();
    http.expectNone(r => r.url.endsWith('/pick'));
    click('Reload round');
    load(proposal());
    expect(page()['filters']().playerCount).toBeNull();
  });

  it('ignores a slow old-mode response when another mode has loaded', () => {
    load();
    mode('VideoGames');
    const slow = http.expectOne(r => r.url.endsWith('/rounds/VideoGames'));
    mode('BoardGames');
    load(proposal('BoardGames', [board]));
    slow.flush(proposal('VideoGames'));
    expect(page()['currentResult']()).toEqual(board);
  });

  it('cancels old-account requests, clears private data and discards queued edits on logout', () => {
    propose();
    page()['onPlayerCount']('2');
    const pending = http.expectOne(r => r.method === 'PUT');
    page()['onPlayerCount']('3');
    TestBed.inject(AuthService).clearLocalSession();
    fixture.detectChanges();
    expect(pending.cancelled).toBe(true);
    expect(page()['ready']()).toBe(false);
    expect(page()['visibleHistory']()).toEqual([]);
    expect(text()).not.toContain('Hades');
    http.expectNone(r => r.method === 'PUT');
  });

  it('loads the new account from default All, not an old cached revision or round', async () => {
    load(proposal());
    mode('BoardGames');
    load(proposal('BoardGames', [board], 8));
    const session = createTestSession();
    session.user.id = 'another-user';
    mock.emit('SIGNED_IN', session);
    load();
    expect(page()['mode']()).toBe('All');
    expect(page()['visibleHistory']()).toEqual([]);
    click('Pick a game');
    const request = pick();
    expect(request.request.body.roundRevision).toBe(0);
    acknowledge(request, proposal());
    await fixture.whenStable();
  });

  it.each([video, board])('Select Game for $gameType remains explicit and does not reset or log', item => {
    propose(item);
    click('Select Game');
    const selection = http.expectOne(r => r.url.endsWith('/currently-playing'));
    expect(selection.request.body).toEqual({ gameId: item.gameId });
    click('Selecting...');
    http.expectNone(r => r.url.endsWith('/currently-playing'));
    selection.flush({ videoGame: null, boardGame: null });
    fixture.detectChanges();
    expect(text()).toContain('Selected for Currently Playing');
    expect(page()['shownLibraryEntryIds']()).toEqual([item.libraryEntryId]);
    http.expectNone(r => r.url.endsWith('/play-log') || r.url.endsWith('/reset'));
  });

  it.each([404, 409, 503])('handles failed selection %s without claiming success or losing the proposal', status => {
    propose();
    click('Select Game');
    http.expectOne(r => r.url.endsWith('/currently-playing')).flush(null, { status, statusText: 'Failed' });
    fixture.detectChanges();
    expect(page()['currentResult']()).toEqual(video);
    expect(text()).not.toContain('Selected for Currently Playing');
    expect(text()).toContain(status === 503 ? 'Check Currently Playing on Home' : 'no longer available');
  });

  it('cancels pending selection on logout', () => {
    propose();
    click('Select Game');
    const pending = http.expectOne(r => r.url.endsWith('/currently-playing'));
    TestBed.inject(AuthService).clearLocalSession();
    expect(pending.cancelled).toBe(true);
    expect(page()['currentResult']()).toBeNull();
  });

  it('does not attach an old selection failure to a newer proposal', () => {
    propose();
    click('Select Game');
    const selection = http.expectOne(r => r.url.endsWith('/currently-playing'));
    click('Another');
    acknowledge(pick(), proposal('All', [board, video], 2));
    selection.flush(null, { status: 409, statusText: 'Failed' });
    fixture.detectChanges();
    expect(text()).not.toContain('no longer available');
  });

  it.each(['recovery', 'pick', 'selection'])('handles expired authentication during %s', async action => {
    let request: TestRequest;
    if (action === 'recovery') request = http.expectOne(r => r.url.endsWith('/rounds/All'));
    else {
      propose();
      click(action === 'pick' ? 'Another' : 'Select Game');
      request = action === 'pick' ? pick() : http.expectOne(r => r.url.endsWith('/currently-playing'));
    }
    request.flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(page()['visibleHistory']()).toEqual([]);
  });

  it.each([false, true])('renders Family friendly chips only when true (%s)', value => {
    const first = { ...video, isFamilyFriendly: value };
    const second = { ...board, isFamilyFriendly: !value };
    load(proposal('All', [first, second], 2));
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('.random-picker-card--current .family-friendly-chip')?.textContent ?? null)
      .toBe(value ? 'Family friendly' : null);
    expect(host.querySelector('.random-picker-card--history .family-friendly-chip')?.textContent ?? null)
      .toBe(value ? null : 'Family friendly');
  });

  it.each([video, board])('has no logging action or dialog for $gameType; Another and Select Game do not log', item => {
    propose(item);
    expect(text()).not.toContain('Log play');
    expect(text()).not.toContain('Log Play Time');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-log-play-dialog')).toBeNull();
    click('Select Game');
    http.expectOne(r => r.url.endsWith('/currently-playing')).flush({ videoGame: null, boardGame: null });
    fixture.detectChanges();
    click('Another');
    acknowledge(pick(), round('All', { revision: 2, state: 'ALL_ALREADY_SHOWN', history: [item], shownLibraryEntryIds: [item.libraryEntryId] }));
    expect(page()['visibleHistory']()).toEqual([item]);
    expect(page()['shownLibraryEntryIds']()).toEqual([item.libraryEntryId]);
    http.expectNone(r => r.url.endsWith('/play-log'));
  });

  it.each<RandomPickerMode>(['All', 'VideoGames', 'BoardGames'])('persists mode-specific filter controls in %s without dropping Family friendly', value => {
    load();
    if (value !== 'All') mode(value);
    else page()['reloadRound']();
    load(round(value));
    const context: RandomPickerFilters = { ...filters, familyFriendlyOnly: true, playerCount: 2 };
    if (value === 'VideoGames') Object.assign(context, { platformIds: ['p1'], genreIds: ['g1'], gameStatuses: ['Playing'] });
    if (value === 'BoardGames') Object.assign(context, { availableDuration: 60, interactionTypes: ['Cooperative'] });
    if (value === 'All') context.ratingMin = 4;
    page()['filters'].set(context);
    page()['onFamilyFriendlyOnly'](true);
    const saved = save(round(value, { revision: 1, filters: context }));
    expect(saved.request.body).toMatchObject(context);
    page()['reloadRound']();
    load(round(value, { revision: 1, filters: context }));
    expect(page()['filters']()).toEqual(context);
  });
});
