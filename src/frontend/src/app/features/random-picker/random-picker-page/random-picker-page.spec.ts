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

import { RandomPickerItem } from '../random-picker';

import { RandomPickerPage } from './random-picker-page';

const videoResult: RandomPickerItem = {
  libraryEntryId: 'entry-1',
  gameId: 'game-1',
  gameType: 'VideoGame',
  name: 'Hades',
  coverImageUrl: null,
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
