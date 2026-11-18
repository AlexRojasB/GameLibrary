import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { authGuard } from '../../../core/auth/auth.guard';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { routes } from '../../../app.routes';

import { RandomPickerItem, RandomPickerPickResponse } from '../random-picker';

import { RandomPickerPage } from './random-picker-page';

const videoResult: RandomPickerItem = {
  libraryEntryId: 'entry-1',
  gameId: 'game-1',
  gameType: 'VideoGame',
  name: 'Hades',
  coverImageUrl: null,
  coverUploadId: null,
  isFamilyFriendly: false,
  rating: 5,
  notes: 'Fast runs',
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  minimumPlayers: 1,
  maximumPlayers: 1,
  approximateDuration: null,
  interactionType: null,
};

const boardResult: RandomPickerItem = {
  libraryEntryId: 'entry-2',
  gameId: 'game-2',
  gameType: 'BoardGame',
  name: 'Pandemic',
  coverImageUrl: 'https://example.com/pandemic.jpg',
  coverUploadId: null,
  isFamilyFriendly: true,
  rating: 4,
  notes: null,
  platformIds: [],
  genreIds: [],
  gameStatus: null,
  progressPercentage: null,
  minimumPlayers: 2,
  maximumPlayers: 4,
  approximateDuration: 45,
  interactionType: 'Cooperative',
};

@Component({ selector: 'app-random-picker-login-stub', template: '' })
class LoginStub {}

@Component({ selector: 'app-random-picker-destination-stub', template: '' })
class DestinationStub {}

function configure(): { fixture: ComponentFixture<RandomPickerPage>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [RandomPickerPage],
    providers: [
      provideRouter([
        { path: 'login', component: LoginStub },
        { path: 'video-games', component: DestinationStub },
        { path: 'board-games', component: DestinationStub },
      ]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ],
  });
  const fixture = TestBed.createComponent(RandomPickerPage);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushLookups(httpMock: HttpTestingController): void {
  httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/platforms')).flush([{ id: 'p1', name: 'Steam' }]);
  httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/genres')).flush([{ id: 'g1', name: 'Action' }]);
}

function pickRequest(httpMock: HttpTestingController): TestRequest {
  return httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/random-picker/pick'));
}

function playLogRequests(httpMock: HttpTestingController): TestRequest[] {
  return httpMock.match((request) => request.method === 'POST' && request.url.endsWith('/play-log'));
}

function text(fixture: ComponentFixture<RandomPickerPage>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function buttonByText(fixture: ComponentFixture<RandomPickerPage>, label: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (!button) {
    throw new Error(`No button with text "${label}"`);
  }
  return button as HTMLButtonElement;
}

function submitLogDialog(fixture: ComponentFixture<RandomPickerPage>, playedAt = '2026-08-24T18:30', durationMinutes = '90'): void {
  const host = (fixture.nativeElement as HTMLElement).querySelector('app-log-play-dialog') as HTMLElement;
  const playedAtInput = host.querySelector('input[type="datetime-local"]') as HTMLInputElement;
  const durationInput = host.querySelector('input[type="number"]') as HTMLInputElement;

  playedAtInput.value = playedAt;
  playedAtInput.dispatchEvent(new Event('input'));
  durationInput.value = durationMinutes;
  durationInput.dispatchEvent(new Event('input'));
  fixture.detectChanges();

  (host.querySelector('button[type="submit"]') as HTMLButtonElement).click();
  fixture.detectChanges();
}

function modeSelect(fixture: ComponentFixture<RandomPickerPage>): HTMLSelectElement {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')].find((select) =>
    [...select.options].some((option) => option.value === 'VideoGames'),
  ) as HTMLSelectElement;
}

function setMode(fixture: ComponentFixture<RandomPickerPage>, value: string): void {
  const select = modeSelect(fixture);
  select.value = value;
  select.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

function selectMultipleByLabel(fixture: ComponentFixture<RandomPickerPage>, label: string, values: string[]): void {
  const labels = [...(fixture.nativeElement as HTMLElement).querySelectorAll('label')];
  const target = labels.find((candidate) => candidate.textContent?.includes(label))?.querySelector('select') as HTMLSelectElement;
  for (const option of [...target.options]) {
    option.selected = values.includes(option.value);
  }
  target.dispatchEvent(new Event('change'));
  fixture.detectChanges();
}

function setNumberByLabel(fixture: ComponentFixture<RandomPickerPage>, label: string, value: string): void {
  const labels = [...(fixture.nativeElement as HTMLElement).querySelectorAll('label')];
  const input = labels.find((candidate) => candidate.textContent?.includes(label))?.querySelector('input') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function currentCard(fixture: ComponentFixture<RandomPickerPage>): HTMLElement | null {
  return (fixture.nativeElement as HTMLElement).querySelector('.random-picker-card--current');
}

function historyCards(fixture: ComponentFixture<RandomPickerPage>): HTMLElement[] {
  return [...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('.random-picker-card--history')];
}

function pickerState(fixture: ComponentFixture<RandomPickerPage>) {
  const page = fixture.componentInstance;
  return {
    result: page['currentResult'](),
    state: page['currentState'](),
    loading: page['loading'](),
    error: page['error'](),
    shownIds: page['shownLibraryEntryIds'](),
    history: page['visibleHistory'](),
    logPendingGameId: page['logPendingGameId'](),
    logSuccess: page['logSuccess'](),
    logError: page['logError'](),
  };
}

describe('RandomPickerPage', () => {
  let fixture: ComponentFixture<RandomPickerPage>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = configure();
    fixture = setup.fixture;
    httpMock = setup.httpMock;
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('is protected by authGuard in app routes', () => {
    expect(routes.find((route) => route.path === 'random-picker')?.canActivate).toEqual([authGuard]);

    flushLookups(httpMock);
  });

  it('shows mode-specific controls and clears unavailable filters when mode changes', async () => {
    flushLookups(httpMock);
    expect(text(fixture)).toContain('Minimum rating');
    expect(text(fixture)).toContain('Player count');
    expect(text(fixture)).not.toContain('Platforms');

    setMode(fixture, 'VideoGames');
    expect(text(fixture)).toContain('Platforms');
    selectMultipleByLabel(fixture, 'Platforms', ['p1']);

    setMode(fixture, 'BoardGames');
    expect(text(fixture)).toContain('Available duration');
    expect(text(fixture)).not.toContain('Platforms');

    buttonByText(fixture, 'Pick a game').click();
    const req = pickRequest(httpMock);
    expect(req.request.body.mode).toBe('BoardGames');
    expect(req.request.body.platformIds).toEqual([]);
    req.flush({ state: 'NO_CANDIDATES', result: null });
    await fixture.whenStable();
  });

  it('displays a successful result and sends accumulated shown history for Another', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    const first = pickRequest(httpMock);
    expect(first.request.body).toMatchObject({ mode: 'All', shownLibraryEntryIds: [] });
    first.flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Hades');
    expect(text(fixture)).toContain('Video game');
    expect(text(fixture)).toContain('Steam');
    expect(text(fixture)).toContain('Action');
    expect(text(fixture)).toContain('Notes: Fast runs');
    expect(text(fixture)).toContain('1 player');
    expect(text(fixture)).toContain('Log play');

    buttonByText(fixture, 'Another').click();
    const another = pickRequest(httpMock);
    expect(another.request.body.shownLibraryEntryIds).toEqual(['entry-1']);
    another.flush({ state: 'SUCCESS', result: boardResult });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Pandemic');
    expect(text(fixture)).toContain('2-4 players');
    expect(text(fixture)).toContain('45 min');
    expect(currentCard(fixture)?.textContent).toContain('Pandemic');
    expect(historyCards(fixture).map((card) => card.textContent ?? '')).toEqual([expect.stringContaining('Hades')]);
  });


  it.each([false, true])('renders positive chips only for true current and history values (first=%s)', async (value) => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: { ...videoResult, isFamilyFriendly: value } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(currentCard(fixture)?.querySelector('.family-friendly-chip')?.textContent ?? null)
      .toBe(value ? 'Family friendly' : null);

    buttonByText(fixture, 'Another').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: { ...boardResult, isFamilyFriendly: !value } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(currentCard(fixture)?.querySelector('.family-friendly-chip')?.textContent ?? null)
      .toBe(value ? null : 'Family friendly');
    expect(historyCards(fixture)[0].querySelector('.family-friendly-chip')?.textContent ?? null)
      .toBe(value ? 'Family friendly' : null);
    expect(text(fixture)).not.toContain('Not family friendly');
  });

  it.each(['All', 'VideoGames', 'BoardGames'])('preserves session, other filters, result states and reset with Family friendly in %s', async (mode) => {
    flushLookups(httpMock);
    setMode(fixture, mode);
    buttonByText(fixture, 'Filter this pick').click();
    fixture.detectChanges();
    const checkbox = (fixture.nativeElement as HTMLElement).querySelector('.random-picker__family-filter input') as HTMLInputElement;
    expect(checkbox.checked).toBe(false);
    expect(checkbox.labels?.[0].textContent).toContain('Family friendly only');
    setNumberByLabel(fixture, 'Player count', '2');
    const firstResult = mode === 'BoardGames' ? boardResult : videoResult;

    buttonByText(fixture, 'Pick a game').click();
    const initial = pickRequest(httpMock);
    expect(initial.request.body).toMatchObject({ mode, familyFriendlyOnly: false, playerCount: 2, shownLibraryEntryIds: [] });
    initial.flush({ state: 'SUCCESS', result: firstResult });
    await fixture.whenStable();
    fixture.detectChanges();

    checkbox.click();
    fixture.detectChanges();
    expect(currentCard(fixture)).toBeNull();
    expect(historyCards(fixture).map((card) => card.textContent)).toEqual([expect.stringContaining(firstResult.name)]);
    expect(text(fixture)).toContain('2 picker filters active');
    httpMock.expectNone((request) => request.url.endsWith('/random-picker/pick'));
    buttonByText(fixture, 'Pick a game').click();
    const filtered = pickRequest(httpMock);
    expect(filtered.request.body).toMatchObject({ mode, familyFriendlyOnly: true, playerCount: 2, shownLibraryEntryIds: [firstResult.libraryEntryId] });
    const nextResult = { ...firstResult, libraryEntryId: 'next-entry', gameId: 'next-game', name: 'Next family game', isFamilyFriendly: true };
    filtered.flush({ state: 'SUCCESS', result: nextResult });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(currentCard(fixture)?.textContent).toContain('Next family game');

    buttonByText(fixture, 'Another').click();
    const exhausted = pickRequest(httpMock);
    const shownIds = [firstResult.libraryEntryId, 'next-entry'];
    expect(exhausted.request.body).toMatchObject({ familyFriendlyOnly: true, shownLibraryEntryIds: shownIds });
    expect(exhausted.request.body).not.toHaveProperty('visibleHistory');
    exhausted.flush({ state: 'ALL_ALREADY_SHOWN', result: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('All matching games already shown');
    expect(historyCards(fixture).map((card) => card.textContent)).toEqual([
      expect.stringContaining('Next family game'), expect.stringContaining(firstResult.name),
    ]);

    checkbox.click();
    fixture.detectChanges();
    buttonByText(fixture, 'Pick a game').click();
    const unfiltered = pickRequest(httpMock);
    expect(unfiltered.request.body).toMatchObject({ familyFriendlyOnly: false, playerCount: 2, shownLibraryEntryIds: shownIds });
    unfiltered.flush({ state: 'NO_CANDIDATES', result: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('No owned games match');
    expect(text(fixture)).not.toContain('All matching games already shown');
    expect(historyCards(fixture)).toHaveLength(2);

    checkbox.click();
    fixture.detectChanges();
    buttonByText(fixture, 'Pick a game').click();
    const again = pickRequest(httpMock);
    expect(again.request.body).toMatchObject({ familyFriendlyOnly: true, shownLibraryEntryIds: shownIds });
    again.flush({ state: 'ALL_ALREADY_SHOWN', result: null });
    await fixture.whenStable();
    fixture.detectChanges();
    buttonByText(fixture, 'Start over').click();
    fixture.detectChanges();
    expect(historyCards(fixture)).toHaveLength(0);
    expect(currentCard(fixture)).toBeNull();
    expect(checkbox.checked).toBe(true);

    buttonByText(fixture, 'Pick a game').click();
    const reset = pickRequest(httpMock);
    expect(reset.request.body).toMatchObject({ familyFriendlyOnly: true, playerCount: 2, shownLibraryEntryIds: [] });
    reset.flush({ state: 'SUCCESS', result: nextResult });
    await fixture.whenStable();
    fixture.detectChanges();
    buttonByText(fixture, 'Clear filters').click();
    fixture.detectChanges();
    expect(checkbox.checked).toBe(false);
    expect(text(fixture)).toContain('No picker filters active');
    expect(historyCards(fixture)).toHaveLength(1);
    buttonByText(fixture, 'Pick a game').click();
    const cleared = pickRequest(httpMock);
    expect(cleared.request.body).toMatchObject({ familyFriendlyOnly: false, playerCount: null, shownLibraryEntryIds: ['next-entry'] });
    cleared.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('keeps Family friendly active through mode changes while clearing type-specific filters', async () => {
    flushLookups(httpMock);
    setMode(fixture, 'VideoGames');
    selectMultipleByLabel(fixture, 'Platforms', ['p1']);
    selectMultipleByLabel(fixture, 'Genres', ['g1']);
    selectMultipleByLabel(fixture, 'Game status', ['Playing']);
    const checkbox = (fixture.nativeElement as HTMLElement).querySelector('.random-picker__family-filter input') as HTMLInputElement;
    checkbox.click();
    fixture.detectChanges();
    buttonByText(fixture, 'Pick a game').click();
    const first = pickRequest(httpMock);
    expect(first.request.body).toMatchObject({ familyFriendlyOnly: true, platformIds: ['p1'], genreIds: ['g1'], gameStatuses: ['Playing'] });
    first.flush({ state: 'SUCCESS', result: { ...videoResult, isFamilyFriendly: true } });
    await fixture.whenStable();
    fixture.detectChanges();

    for (const mode of ['BoardGames', 'All', 'VideoGames']) {
      setMode(fixture, mode);
      expect(checkbox.checked).toBe(true);
      expect(currentCard(fixture)).toBeNull();
      expect(historyCards(fixture)).toHaveLength(1);
      expect(text(fixture)).toContain('1 picker filter active');
      buttonByText(fixture, 'Pick a game').click();
      const req = pickRequest(httpMock);
      expect(req.request.body).toMatchObject({
        mode, familyFriendlyOnly: true, platformIds: [], genreIds: [], gameStatuses: [],
        shownLibraryEntryIds: ['entry-1'],
      });
      req.flush({ state: 'NO_CANDIDATES', result: null });
      await fixture.whenStable();
      fixture.detectChanges();
    }
  });

  it('preserves shown history when filters change and clears the displayed result', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Hades');

    const rating = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')].find((select) =>
      [...select.options].some((option) => option.value === '5'),
    ) as HTMLSelectElement;
    rating.value = '5';
    rating.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    expect(currentCard(fixture)).toBeNull();
    expect(text(fixture)).toContain('Previous picks');
    expect(text(fixture)).toContain('Hades');

    buttonByText(fixture, 'Pick a game').click();
    const req = pickRequest(httpMock);
    expect(req.request.body.ratingMin).toBe(5);
    expect(req.request.body.shownLibraryEntryIds).toEqual(['entry-1']);
    req.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('preserves shown history when mode changes while clearing unavailable filters', async () => {
    flushLookups(httpMock);
    setMode(fixture, 'VideoGames');
    selectMultipleByLabel(fixture, 'Platforms', ['p1']);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    setMode(fixture, 'BoardGames');
    expect(currentCard(fixture)).toBeNull();
    expect(text(fixture)).toContain('Previous picks');
    expect(text(fixture)).toContain('Hades');
    expect(text(fixture)).not.toContain('Platforms');

    buttonByText(fixture, 'Pick a game').click();
    const req = pickRequest(httpMock);
    expect(req.request.body.mode).toBe('BoardGames');
    expect(req.request.body.platformIds).toEqual([]);
    expect(req.request.body.shownLibraryEntryIds).toEqual(['entry-1']);
    req.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('sends playerCount in VideoGames and All modes and clears it only when unavailable', async () => {
    flushLookups(httpMock);
    setNumberByLabel(fixture, 'Player count', '3');
    buttonByText(fixture, 'Pick a game').click();
    let req = pickRequest(httpMock);
    expect(req.request.body.mode).toBe('All');
    expect(req.request.body.playerCount).toBe(3);
    req.flush({ state: 'NO_CANDIDATES', result: null });
    await fixture.whenStable();
    fixture.detectChanges();

    setMode(fixture, 'VideoGames');
    buttonByText(fixture, 'Pick a game').click();
    req = pickRequest(httpMock);
    expect(req.request.body.playerCount).toBe(3);
    req.flush({ state: 'NO_CANDIDATES', result: null });
    await fixture.whenStable();
    fixture.detectChanges();

    setMode(fixture, 'BoardGames');
    buttonByText(fixture, 'Pick a game').click();
    req = pickRequest(httpMock);
    expect(req.request.body.playerCount).toBe(3);
    req.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('renders no-candidates and all-already-shown states distinctly and resets shown history explicitly', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'NO_CANDIDATES', result: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('No owned games match');

    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Another').click();
    pickRequest(httpMock).flush({ state: 'ALL_ALREADY_SHOWN', result: null });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('All matching games already shown');

    buttonByText(fixture, 'Start over').click();
    fixture.detectChanges();
    expect(text(fixture)).not.toContain('Previous picks');
    buttonByText(fixture, 'Pick a game').click();
    const req = pickRequest(httpMock);
    expect(req.request.body.shownLibraryEntryIds).toEqual([]);
    req.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('starts a fresh volatile session for a new page instance', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.destroy();

    fixture = TestBed.createComponent(RandomPickerPage);
    fixture.detectChanges();
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    const req = pickRequest(httpMock);
    expect(req.request.body.shownLibraryEntryIds).toEqual([]);
    req.flush({ state: 'NO_CANDIDATES', result: null });
  });

  it('navigates to existing management pages from results', async () => {
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    await fixture.whenStable();
    expect(router.url).toBe('/video-games');
  });

  it('clears auth and navigates to login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });

  it('shows a predictable API error for bad picker requests', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ detail: 'Mode is invalid.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Invalid picker filters: Mode is invalid.');
  });

  describe('request races', () => {
    const staleResult = { ...videoResult, libraryEntryId: 'stale-entry', gameId: 'stale-game', name: 'Never shown' };
    const staleResponses: RandomPickerPickResponse[] = [
      { state: 'SUCCESS', result: staleResult },
      { state: 'NO_CANDIDATES', result: null },
      { state: 'ALL_ALREADY_SHOWN', result: null },
    ];

    it.each([
      ...staleResponses.map((response) => ({ name: response.state, response, status: 200 })),
      ...[400, 500, 401].map((status) => ({ name: String(status), response: null, status })),
    ].flatMap((outcome) => [true, false].map((staleFirst) => ({ ...outcome, staleFirst }))))(
      'ignores stale $name (stale response first: $staleFirst)', async ({ response, status, staleFirst }) => {
      flushLookups(httpMock);
      buttonByText(fixture, 'Pick a game').click();
      pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
      fixture.detectChanges();
      const auth = TestBed.inject(AuthService);
      const clearSession = vi.spyOn(auth, 'clearLocalSession');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');

      fixture.componentInstance['another']();
      const stale = pickRequest(httpMock);
      // Exercise overlapping calls independently of the template's disabled button.
      fixture.componentInstance['another']();
      const latest = pickRequest(httpMock);
      const completeLatest = () => latest.flush({ state: 'SUCCESS', result: boardResult });
      if (!staleFirst) {
        completeLatest();
      }
      const before = pickerState(fixture);
      if (status === 200) {
        stale.flush(response);
      } else {
        stale.flush({ detail: 'Stale failure' }, { status, statusText: 'Error' });
      }
      await fixture.whenStable();
      expect(pickerState(fixture)).toEqual(before);
      expect(clearSession).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
      if (staleFirst) {
        expect(pickerState(fixture).loading).toBe(true);
        completeLatest();
      }
      expect(pickerState(fixture)).toMatchObject({
        result: boardResult, state: 'SUCCESS', loading: false, error: '',
        shownIds: ['entry-1', 'entry-2'], history: [boardResult, videoResult],
      });

      fixture.componentInstance['another']();
      const next = pickRequest(httpMock);
      expect(next.request.body.shownLibraryEntryIds).toEqual(['entry-1', 'entry-2']);
      next.flush({ state: 'ALL_ALREADY_SHOWN', result: null });
      fixture.detectChanges();
      expect(currentCard(fixture)).toBeNull();
      expect(historyCards(fixture).map((card) => card.textContent)).toEqual([
        expect.stringContaining('Pandemic'), expect.stringContaining('Hades'),
      ]);
      expect(text(fixture)).not.toContain('Never shown');
    });

    it.each(['NO_CANDIDATES', 'ALL_ALREADY_SHOWN', 'error'])('does not overwrite a newer %s with a stale success', (outcome) => {
      flushLookups(httpMock);
      fixture.componentInstance['pick']();
      pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
      fixture.componentInstance['another']();
      const stale = pickRequest(httpMock);
      fixture.componentInstance['another']();
      const latest = pickRequest(httpMock);
      if (outcome === 'error') {
        latest.flush({ detail: 'Current failure' }, { status: 400, statusText: 'Bad Request' });
        expect(pickerState(fixture).error).toBe('Invalid picker filters: Current failure');
      } else {
        latest.flush({ state: outcome, result: null });
        expect(pickerState(fixture).state).toBe(outcome);
      }
      const before = pickerState(fixture);
      stale.flush({ state: 'SUCCESS', result: staleResult });
      expect(pickerState(fixture)).toEqual(before);
      expect(before.loading).toBe(false);
      expect(before.history).toEqual([videoResult]);
      expect(before.shownIds).toEqual(['entry-1']);
    });

    it.each(['All', 'VideoGames', 'BoardGames'])('invalidates pending picks on Family friendly changes in %s without losing completed history', (mode) => {
      flushLookups(httpMock);
      setMode(fixture, mode);
      const firstResult = mode === 'BoardGames' ? boardResult : videoResult;
      const nextResult = { ...firstResult, libraryEntryId: 'next-entry', gameId: 'next-game', name: 'Next pick' };
      fixture.componentInstance['pick']();
      pickRequest(httpMock).flush({ state: 'SUCCESS', result: firstResult });
      fixture.componentInstance['another']();
      const stale = pickRequest(httpMock);
      const checkbox = (fixture.nativeElement as HTMLElement).querySelector('.random-picker__family-filter input') as HTMLInputElement;
      checkbox.click();
      fixture.detectChanges();
      expect(pickerState(fixture)).toMatchObject({
        loading: false, result: null, state: null, error: '',
        history: [firstResult], shownIds: [firstResult.libraryEntryId],
      });
      // Returning to the original filters must not revalidate the old request.
      checkbox.click();
      fixture.detectChanges();
      const before = pickerState(fixture);
      stale.flush({ state: 'SUCCESS', result: staleResult });
      expect(pickerState(fixture)).toEqual(before);
      buttonByText(fixture, 'Pick a game').click();
      const latest = pickRequest(httpMock);
      expect(latest.request.body).toMatchObject({ mode, familyFriendlyOnly: false, shownLibraryEntryIds: [firstResult.libraryEntryId] });
      latest.flush({ state: 'SUCCESS', result: nextResult });
      expect(pickerState(fixture).history).toEqual([nextResult, firstResult]);
    });

    it.each(['player count', 'platform', 'mode', 'clear', 'reset'])('invalidates outstanding responses on %s and allows a new pick immediately', async (change) => {
      flushLookups(httpMock);
      setMode(fixture, 'VideoGames');
      selectMultipleByLabel(fixture, 'Platforms', ['p1']);
      setNumberByLabel(fixture, 'Player count', '2');
      fixture.componentInstance['pick']();
      pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
      const pending: TestRequest[] = [];
      for (let i = 0; i < 3; i++) {
        fixture.componentInstance['another']();
        pending.push(pickRequest(httpMock));
      }

      if (change === 'player count') {
        setNumberByLabel(fixture, 'Player count', '3');
      } else if (change === 'platform') {
        selectMultipleByLabel(fixture, 'Platforms', []);
      } else if (change === 'mode') {
        setMode(fixture, 'BoardGames');
      } else if (change === 'clear') {
        buttonByText(fixture, 'Clear filters').click();
      } else {
        // Start over is disabled while loading, but the action must still invalidate requests.
        fixture.componentInstance['resetShownHistory']();
      }
      fixture.detectChanges();
      const reset = change === 'reset';
      expect(pickerState(fixture)).toMatchObject({
        loading: false, result: null, state: null, error: '',
        history: reset ? [] : [videoResult], shownIds: reset ? [] : ['entry-1'],
      });
      expect(buttonByText(fixture, 'Pick a game').disabled).toBe(false);
      const before = pickerState(fixture);
      pending[0].flush({ state: 'SUCCESS', result: staleResult });
      pending[1].flush({ detail: 'Old filters' }, { status: 400, statusText: 'Bad Request' });
      pending[2].flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
      await fixture.whenStable();
      expect(pickerState(fixture)).toEqual(before);
      expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
      expect(TestBed.inject(Router).url).not.toBe('/login');

      buttonByText(fixture, 'Pick a game').click();
      const latest = pickRequest(httpMock);
      expect(latest.request.body).toMatchObject({
        mode: change === 'mode' ? 'BoardGames' : 'VideoGames',
        platformIds: ['platform', 'mode', 'clear'].includes(change) ? [] : ['p1'],
        playerCount: change === 'clear' ? null : change === 'player count' ? 3 : 2,
        shownLibraryEntryIds: reset ? [] : ['entry-1'],
      });
      const nextResult = change === 'mode'
        ? boardResult
        : { ...videoResult, libraryEntryId: 'next-entry', gameId: 'next-game', name: 'Next pick', minimumPlayers: 1, maximumPlayers: 4 };
      latest.flush({ state: 'SUCCESS', result: nextResult });
      expect(pickerState(fixture).history).toEqual(reset ? [nextResult] : [nextResult, videoResult]);
    });

    it('tears down outstanding picks when the page is destroyed', () => {
      flushLookups(httpMock);
      const clearSession = vi.spyOn(TestBed.inject(AuthService), 'clearLocalSession');
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl');
      fixture.componentInstance['pick']();
      const first = pickRequest(httpMock);
      fixture.componentInstance['pick']();
      const second = pickRequest(httpMock);
      fixture.destroy();
      expect(first.cancelled).toBe(true);
      expect(second.cancelled).toBe(true);
      expect(clearSession).not.toHaveBeenCalled();
      expect(navigate).not.toHaveBeenCalled();
    });
  });

  it('logs only after explicit current-result dialog confirmation; Pick and Another do not log', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    expect(playLogRequests(httpMock)).toHaveLength(0);
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Another').click();
    expect(playLogRequests(httpMock)).toHaveLength(0);
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: boardResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Log play').click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Confirm when you played');
    expect(playLogRequests(httpMock)).toHaveLength(0);
    submitLogDialog(fixture);
    const requests = playLogRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body).toEqual({
      gameId: 'game-2',
      playedAt: new Date('2026-08-24T18:30').toISOString(),
      durationMinutes: 90,
    });
    requests[0].flush({ id: 'log-1', libraryEntryId: 'entry-2', gameId: 'game-2', gameType: 'BoardGame', gameName: 'Pandemic', coverImageUrl: null, playedAt: '2026-08-24T18:30:00Z', durationMinutes: 90, createdAt: '2026-08-24T18:30:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Play logged.');
  });

  it('prevents duplicate Random Picker Log play posts and keeps Another usable', async () => {
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Log play').click();
    fixture.detectChanges();
    submitLogDialog(fixture);
    buttonByText(fixture, 'Logging...').click();
    fixture.detectChanges();

    let requests = playLogRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(buttonByText(fixture, 'Another').disabled).toBe(false);

    buttonByText(fixture, 'Another').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: boardResult });
    await fixture.whenStable();
    fixture.detectChanges();

    requests[0].flush({ id: 'log-1', libraryEntryId: 'entry-1', gameId: 'game-1', gameType: 'VideoGame', gameName: 'Hades', coverImageUrl: null, playedAt: '2026-08-24T18:30:00Z', durationMinutes: 90, createdAt: '2026-08-24T18:30:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).not.toContain('Play logged.');
    buttonByText(fixture, 'Log play').click();
    fixture.detectChanges();
    submitLogDialog(fixture, '2026-08-24T19:00', '');
    requests = playLogRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body).toEqual({
      gameId: 'game-2',
      playedAt: new Date('2026-08-24T19:00').toISOString(),
      durationMinutes: null,
    });
    requests[0].flush({ id: 'log-2', libraryEntryId: 'entry-2', gameId: 'game-2', gameType: 'BoardGame', gameName: 'Pandemic', coverImageUrl: null, playedAt: '2026-08-24T18:30:00Z', durationMinutes: null, createdAt: '2026-08-24T18:30:00Z' });
  });

  it('shows readable Random Picker Log play errors and handles 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    buttonByText(fixture, 'Pick a game').click();
    pickRequest(httpMock).flush({ state: 'SUCCESS', result: videoResult });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Log play').click();
    fixture.detectChanges();
    submitLogDialog(fixture);
    playLogRequests(httpMock)[0].flush({ detail: 'Only owned games can be logged.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Only owned games can be logged.');

    buttonByText(fixture, 'Log play').click();
    fixture.detectChanges();
    submitLogDialog(fixture);
    playLogRequests(httpMock)[0].flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });
});
