import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { authGuard } from '../../../core/auth/auth.guard';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { routes } from '../../../app.routes';

import { LibraryItem } from '../library';

import { LibraryPage } from './library-page';

const videoGame: LibraryItem = {
  id: 'v1',
  gameType: 'VideoGame',
  name: 'Hades',
  coverImageUrl: null,
  createdAt: '2026-08-17T00:00:00Z',
  acquisitionStatus: 'Owned',
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

const boardGame: LibraryItem = {
  id: 'b1',
  gameType: 'BoardGame',
  name: 'Pandemic',
  coverImageUrl: 'https://example.com/pandemic.jpg',
  createdAt: '2026-08-18T00:00:00Z',
  acquisitionStatus: 'Wishlist',
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

@Component({ selector: 'app-library-login-stub', template: '' })
class LoginStub {}

@Component({ selector: 'app-library-destination-stub', template: '' })
class DestinationStub {}

function configure(): { fixture: ComponentFixture<LibraryPage>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [LibraryPage],
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
  const fixture = TestBed.createComponent(LibraryPage);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushLookups(httpMock: HttpTestingController): void {
  httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/platforms')).flush([{ id: 'p1', name: 'Steam' }]);
  httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/genres')).flush([{ id: 'g1', name: 'Action' }]);
}

function libraryRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/library'));
}

function playLogRequests(httpMock: HttpTestingController) {
  return httpMock.match((request) => request.method === 'POST' && request.url.endsWith('/play-log'));
}

function text(fixture: ComponentFixture<LibraryPage>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function buttonByText(fixture: ComponentFixture<LibraryPage>, label: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  if (!button) {
    throw new Error(`No button with text "${label}"`);
  }
  return button as HTMLButtonElement;
}

function submitLogDialog(fixture: ComponentFixture<LibraryPage>, playedAt = '2026-08-24T18:30', durationMinutes = '90'): void {
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

describe('LibraryPage', () => {
  let fixture: ComponentFixture<LibraryPage>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = configure();
    fixture = setup.fixture;
    httpMock = setup.httpMock;
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  it('is protected by authGuard in app routes', () => {
    expect(routes.find((route) => route.path === 'library')?.canActivate).toEqual([authGuard]);

    flushLookups(httpMock);
    libraryRequest(httpMock).flush([]);
  });

  it('shows loading while the library request is pending', () => {
    expect(text(fixture)).toContain('Loading');

    flushLookups(httpMock);
    libraryRequest(httpMock).flush([]);
  });

  it('renders returned items with type-specific details and resolved names', async () => {
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame, boardGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const body = text(fixture);
    expect(body).toContain('Hades');
    expect(body).toContain('Video game');
    expect(body).toContain('Steam');
    expect(body).toContain('Action');
    expect(body).toContain('Status: Playing');
    expect(body).toContain('Progress: 50%');
    expect(body).toContain('1 player');
    expect(body).toContain('Pandemic');
    expect(body).toContain('Board game');
    expect(body).toContain('2-4 players');
    expect(body).toContain('45 min');
    expect(body).toContain('Cooperative');
    expect(body).toContain('No cover');
    expect(body).toContain('Log play');
  });

  it('distinguishes empty library from no results', async () => {
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(text(fixture)).toContain('No games in your library yet');

    vi.useFakeTimers();
    const input = (fixture.nativeElement as HTMLElement).querySelector('input[type="search"]') as HTMLInputElement;
    input.value = 'missing';
    input.dispatchEvent(new Event('input'));
    vi.runAllTimers();
    fixture.detectChanges();

    const req = libraryRequest(httpMock);
    expect(req.request.params.get('search')).toBe('missing');
    req.flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('No games match your search and filters');
  });

  it('refetches for filter and sort changes, and Clear filters keeps the sort', async () => {
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const sort = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')].find((select) =>
      [...select.options].some((option) => option.value === 'RecentlyAdded'),
    ) as HTMLSelectElement;
    sort.value = 'RecentlyAdded';
    sort.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const sortReq = libraryRequest(httpMock);
    expect(sortReq.request.params.get('sort')).toBe('RecentlyAdded');
    sortReq.flush([videoGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const gameType = [...(fixture.nativeElement as HTMLElement).querySelectorAll('select')].find((select) =>
      [...select.options].some((option) => option.value === 'VideoGame'),
    ) as HTMLSelectElement;
    gameType.value = 'VideoGame';
    gameType.dispatchEvent(new Event('change'));
    fixture.detectChanges();

    const filterReq = libraryRequest(httpMock);
    expect(filterReq.request.params.get('gameType')).toBe('VideoGame');
    expect(filterReq.request.params.get('sort')).toBe('RecentlyAdded');
    filterReq.flush([videoGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Clear filters').click();
    fixture.detectChanges();

    const clearReq = libraryRequest(httpMock);
    expect(clearReq.request.params.get('gameType')).toBeNull();
    expect(clearReq.request.params.get('sort')).toBe('RecentlyAdded');
    clearReq.flush([videoGame]);
  });

  it('shows an error with retry on non-401 failures', async () => {
    flushLookups(httpMock);
    libraryRequest(httpMock).flush({ detail: 'Sort is invalid.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Invalid filters: Sort is invalid.');
    buttonByText(fixture, 'Try again').click();
    fixture.detectChanges();

    libraryRequest(httpMock).flush([videoGame]);
  });

  it('clears the session and navigates to login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    libraryRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });

  it('navigates Edit to the existing management pages', async () => {
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame, boardGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const editButtons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].filter(
      (button) => button.textContent?.trim() === 'Edit',
    ) as HTMLButtonElement[];

    editButtons[0].click();
    await fixture.whenStable();
    expect(router.url).toBe('/video-games');

    editButtons[1].click();
    await fixture.whenStable();
    expect(router.url).toBe('/board-games');
  });

  it('shows Log play only for Owned cards and posts only after dialog confirmation', async () => {
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame, boardGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].filter((button) =>
      button.textContent?.includes('Log play'),
    ) as HTMLButtonElement[];
    expect(buttons).toHaveLength(1);

    buttons[0].click();
    buttons[0].click();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Confirm when you played');
    expect(playLogRequests(httpMock)).toHaveLength(0);
    submitLogDialog(fixture);

    const requests = playLogRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body).toEqual({
      gameId: 'v1',
      playedAt: new Date('2026-08-24T18:30').toISOString(),
      durationMinutes: 90,
    });
    expect(buttons[0].disabled).toBe(true);

    requests[0].flush({ id: 'log-1', libraryEntryId: 'entry-1', gameId: 'v1', gameType: 'VideoGame', gameName: 'Hades', coverImageUrl: null, playedAt: '2026-08-24T18:30:00Z', durationMinutes: 90, createdAt: '2026-08-24T18:30:00Z' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Play logged.');
    expect(buttons[0].disabled).toBe(false);
  });

  it('keeps unrelated Library card Log play actions usable while one card is pending', async () => {
    const secondOwned = { ...boardGame, id: 'b2', name: 'Catan', acquisitionStatus: 'Owned' as const };
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame, secondOwned]);
    await fixture.whenStable();
    fixture.detectChanges();

    const buttons = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].filter((button) =>
      button.textContent?.includes('Log play'),
    ) as HTMLButtonElement[];

    buttons[0].click();
    fixture.detectChanges();
    submitLogDialog(fixture, '2026-08-24T18:30', '45');
    fixture.detectChanges();
    expect(buttons[0].disabled).toBe(true);
    expect(buttons[1].disabled).toBe(false);

    buttons[1].click();
    fixture.detectChanges();
    submitLogDialog(fixture, '2026-08-24T19:00', '');
    const requests = playLogRequests(httpMock);
    expect(requests).toHaveLength(2);
    expect(requests.map((request) => request.request.body.gameId).sort()).toEqual(['b2', 'v1']);
    expect(requests.map((request) => request.request.body.durationMinutes).sort()).toEqual([45, null]);
    requests.forEach((request) => request.flush({ id: 'log', libraryEntryId: 'entry', gameId: request.request.body.gameId, gameType: 'VideoGame', gameName: 'Game', coverImageUrl: null, playedAt: '2026-08-24T18:30:00Z', durationMinutes: request.request.body.durationMinutes, createdAt: '2026-08-24T18:30:00Z' }));
  });

  it('shows readable Log play errors and handles 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    flushLookups(httpMock);
    libraryRequest(httpMock).flush([videoGame]);
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
