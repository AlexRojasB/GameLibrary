import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../../core/auth/testing/supabase-client.mock';

import { BoardGame } from '../../board-game';

import { BoardGamesList } from './board-games-list';

const catan: BoardGame = {
  id: '1',
  name: 'Catan',
  coverImageUrl: null,
  minimumPlayers: 3,
  maximumPlayers: 4,
  approximateDuration: 60,
  interactionType: 'Cooperative',
  acquisitionStatus: 'Owned',
  rating: 5,
  notes: 'Trading',
  createdAt: '2026-08-17T00:00:00Z',
};

const soloGame: BoardGame = {
  id: '2',
  name: 'Solo',
  coverImageUrl: null,
  minimumPlayers: 1,
  maximumPlayers: 1,
  approximateDuration: null,
  interactionType: null,
  acquisitionStatus: 'Wishlist',
  rating: null,
  notes: null,
  createdAt: '2026-08-17T00:00:00Z',
};

@Component({ template: '' })
class LoginStub {}

function configure(): { fixture: ComponentFixture<BoardGamesList>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [BoardGamesList],
    providers: [
      provideRouter([{ path: 'login', component: LoginStub }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ],
  });
  const fixture = TestBed.createComponent(BoardGamesList);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function listRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/board-games'));
}

function buttonByText(fixture: ComponentFixture<BoardGamesList>, text: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`No button with text "${text}"`);
  }
  return button as HTMLButtonElement;
}

function setName(fixture: ComponentFixture<BoardGamesList>, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-name') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function setNumber(fixture: ComponentFixture<BoardGamesList>, selector: string, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('BoardGamesList', () => {
  let fixture: ComponentFixture<BoardGamesList>;
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

    listRequest(httpMock).flush([]);
  });

  it('shows the empty state when there are no board games', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No board games yet');
  });

  it('renders the returned board games with player range, duration, and interaction type', async () => {
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Catan');
    expect(text).toContain('3–4 players');
    expect(text).toContain('60 min');
    expect(text).toContain('Cooperative');
    expect(text).toContain('Owned');
    expect(text).toContain('Rating: 5');
  });

  it('renders a single-player label when both player counts are 1', async () => {
    listRequest(httpMock).flush([soloGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('1 player');
    expect(text).not.toContain('min');
  });

  it('shows an error state with a retry action on non-401 failures', async () => {
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Unable to load your board games');
    expect(text).toContain('Try again');
  });

  it('reloads the list when Try again is clicked', async () => {
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Try again').click();
    fixture.detectChanges();

    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Catan');
  });

  it('creates a board game on submit and reloads the list on success', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first board game').click();
    fixture.detectChanges();

    setName(fixture, '  Catan  ');
    setNumber(fixture, '#board-game-form-minimum', '3');
    setNumber(fixture, '#board-game-form-maximum', '4');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/board-games'));
    expect(createReq.request.body).toEqual({
      name: 'Catan',
      minimumPlayers: 3,
      maximumPlayers: 4,
      approximateDuration: null,
      interactionType: 'Competitive',
      acquisitionStatus: 'Owned',
      rating: null,
      notes: null,
      coverImageUrl: null,
    });
    createReq.flush({ ...catan });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...catan }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Catan');
    expect(text).not.toContain('Create');
  });

  it('keeps the form open and shows the server message on a 400 create', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first board game').click();
    fixture.detectChanges();

    setName(fixture, 'Catan');
    setNumber(fixture, '#board-game-form-minimum', '3');
    setNumber(fixture, '#board-game-form-maximum', '4');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/board-games'));
    createReq.flush(
      { detail: 'Maximum players must be at least the minimum players.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Maximum players must be at least the minimum players.');
    expect((fixture.nativeElement as HTMLElement).querySelector('#board-game-form-name')).not.toBeNull();
  });

  it('loads the current name, submits an update, and reloads on success', async () => {
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();

    const nameInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Catan');

    setName(fixture, 'Catan II');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/board-games/1'));
    expect(updateReq.request.body.name).toBe('Catan II');
    updateReq.flush({ ...catan, name: 'Catan II' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...catan, name: 'Catan II' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Catan II');
  });

  it('shows a not-found message and reloads on 404 update', async () => {
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();

    setName(fixture, 'Renamed');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/board-games/1'));
    updateReq.flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ ...catan, name: 'Renamed' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('This board game no longer exists.');
  });

  it('confirms, deletes, and reloads the list on success', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    const deleteReq = httpMock.expectOne((request) => request.method === 'DELETE' && request.url.endsWith('/board-games/1'));
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No board games yet');
  });

  it('does not delete when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    httpMock.expectNone((request) => request.method === 'DELETE');
  });

  it('clears the session and navigates to /login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);

    listRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });
});
