import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

import { VideoGame } from '../video-game';

import { VideoGamesList } from './video-games-list';

const steam = { id: 'p1', name: 'Steam' };
const action = { id: 'g1', name: 'Action' };

const ownedGame: VideoGame = {
  id: '1',
  name: 'Hades',
  coverImageUrl: null,
  acquisitionStatus: 'Owned',
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  rating: 5,
  notes: 'Roguelike',
  createdAt: '2026-08-17T00:00:00Z',
  minimumPlayers: 1,
  maximumPlayers: 2,
};

@Component({ template: '' })
class LoginStub {}

function configure(): { fixture: ComponentFixture<VideoGamesList>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [VideoGamesList],
    providers: [
      provideRouter([{ path: 'login', component: LoginStub }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ],
  });
  const fixture = TestBed.createComponent(VideoGamesList);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function flushSupporting(httpMock: HttpTestingController): void {
  const platforms = httpMock.match((request) => request.method === 'GET' && request.url.endsWith('/platforms'));
  platforms.forEach((request) => request.flush([steam]));
  const genres = httpMock.match((request) => request.method === 'GET' && request.url.endsWith('/genres'));
  genres.forEach((request) => request.flush([action]));
}

function listRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/video-games'));
}

function buttonByText(fixture: ComponentFixture<VideoGamesList>, text: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`No button with text "${text}"`);
  }
  return button as HTMLButtonElement;
}

function setName(fixture: ComponentFixture<VideoGamesList>, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector('#game-form-name') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('VideoGamesList', () => {
  let fixture: ComponentFixture<VideoGamesList>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = configure();
    fixture = setup.fixture;
    httpMock = setup.httpMock;
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('shows a loading state while the list request is pending', () => {
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Loading');

    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
  });

  it('shows the empty state when there are no video games', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No video games yet');
  });

  it('renders the returned video games with platform and genre names', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Hades');
    expect(text).toContain('Steam');
    expect(text).toContain('Action');
    expect(text).toContain('1-2 players');
  });

  it('shows an error state with a retry action on non-401 failures', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Unable to load your video games');
    expect(text).toContain('Try again');
  });

  it('reloads the list when Try again is clicked', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Try again').click();
    fixture.detectChanges();

    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
  });

  it('creates a video game on submit and reloads the list on success', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first video game').click();
    fixture.detectChanges();
    flushSupporting(httpMock);

    setName(fixture, '  Hades  ');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/video-games'));
    expect(createReq.request.body).toEqual({
      name: 'Hades',
      coverImageUrl: null,
      acquisitionStatus: 'Owned',
      platformIds: ['p1'],
      genreIds: [],
      gameStatus: null,
      progressPercentage: null,
      rating: null,
      notes: null,
      minimumPlayers: null,
      maximumPlayers: null,
    });
    createReq.flush({ ...ownedGame });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...ownedGame }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Hades');
    expect(text).not.toContain('Create');
  });

  it('keeps the form open and shows the server message on a 400 create', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first video game').click();
    fixture.detectChanges();
    flushSupporting(httpMock);

    setName(fixture, 'Hades');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/video-games'));
    createReq.flush(
      { detail: 'One or more platforms are not available in your library.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('One or more platforms are not available in your library.');
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-name')).not.toBeNull();
  });

  it('loads the current name, submits an update, and reloads on success', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();
    flushSupporting(httpMock);

    const nameInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Hades');

    setName(fixture, 'Hades II');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/video-games/1'));
    expect(updateReq.request.body.name).toBe('Hades II');
    updateReq.flush({ ...ownedGame, name: 'Hades II' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...ownedGame, name: 'Hades II' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades II');
  });

  it('shows a not-found message and reloads on 404 update', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();
    flushSupporting(httpMock);

    setName(fixture, 'Renamed');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/video-games/1'));
    updateReq.flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...ownedGame, name: 'Renamed' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This video game no longer exists.');
  });

  it('confirms, deletes, and reloads the list on success', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    const deleteReq = httpMock.expectOne((request) => request.method === 'DELETE' && request.url.endsWith('/video-games/1'));
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No video games yet');
  });

  it('does not delete when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    httpMock.expectNone((request) => request.method === 'DELETE');
  });

  it('clears the session and navigates to /login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    flushSupporting(httpMock);

    listRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });
});
