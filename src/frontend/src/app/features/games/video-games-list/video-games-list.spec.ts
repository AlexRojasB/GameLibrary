import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

import { VideoGame, VideoGameInput } from '../video-game';
import { CoverSave } from '../covers/cover-save';

import { VideoGamesList } from './video-games-list';

const steam = { id: 'p1', name: 'Steam' };
const action = { id: 'g1', name: 'Action' };

const ownedGame: VideoGame = {
  id: '1',
  name: 'Hades',
  coverImageUrl: null,
  coverUploadId: null,
  isFamilyFriendly: false,
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

async function configure(): Promise<{ fixture: ComponentFixture<VideoGamesList>; httpMock: HttpTestingController; emitAuth: ReturnType<typeof createMockSupabase>['emit'] }> {
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
  await TestBed.inject(AuthService).initialize();
  const fixture = TestBed.createComponent(VideoGamesList);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock, emitAuth: mock.emit };
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

function searchInput(fixture: ComponentFixture<VideoGamesList>): HTMLInputElement {
  return (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#video-games-search')!;
}

function search(fixture: ComponentFixture<VideoGamesList>, value: string): void {
  const input = searchInput(fixture);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

function searchRequest(httpMock: HttpTestingController, query: string) {
  return httpMock.expectOne(request => request.method === 'GET' && request.url.endsWith('/library')
    && request.params.get('search') === query && request.params.get('gameType') === 'VideoGame'
    && request.params.get('sort') === 'NameAsc');
}

describe('VideoGamesList', () => {
  let fixture: ComponentFixture<VideoGamesList>;
  let httpMock: HttpTestingController;
  let emitAuth: ReturnType<typeof createMockSupabase>['emit'];

  beforeEach(async () => {
    const setup = await configure();
    fixture = setup.fixture;
    httpMock = setup.httpMock;
    emitAuth = setup.emitAuth;
  });

  afterEach(() => {
    httpMock.match((request) => request.url.endsWith('/cover-uploads/options')).forEach((request) => {
      if (!request.cancelled) request.flush({ enabled: false });
    });
    httpMock.verify();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
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

  it('searches the full collection on the server and renders all returned matches in API order', () => {
    flushSupporting(httpMock);
    // This initial response deliberately lacks the eventual match; the UI must
    // query the whole collection rather than filtering the already loaded list.
    listRequest(httpMock).flush([{ ...ownedGame, name: 'Other title' }]);
    fixture.detectChanges();
    expect(searchInput(fixture).labels?.[0].textContent).toBe('Search video games');
    search(fixture, '  aDe  ');
    const matches = Array.from({ length: 51 }, (_, index) => ({ ...ownedGame, id: String(index), name: `Hades ${index}` }));
    searchRequest(httpMock, 'aDe').flush(matches);
    fixture.detectChanges();
    const cards = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.video-games__item strong')];
    expect(cards.map(card => card.textContent)).toEqual(matches.map(game => game.name));
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('51 video games found');
    httpMock.expectNone(request => request.params.has('page') || request.params.has('pageSize'));
  });

  it('distinguishes no matches, keeps Add available, and clears search with keyboard focus restored', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    search(fixture, 'missing');
    searchRequest(httpMock, 'missing').flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No video games match your search');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('No video games yet');
    expect(buttonByText(fixture, 'Add video game')).toBeTruthy();
    buttonByText(fixture, 'Clear search').click();
    expect(document.activeElement).toBe(searchInput(fixture));
    listRequest(httpMock).flush([ownedGame]);
    fixture.detectChanges();
    expect(searchInput(fixture).value).toBe('');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
  });

  it('treats whitespace as no search, without refetching for equivalent surrounding whitespace', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    search(fixture, '  ');
    httpMock.expectNone(request => request.url.endsWith('/library') || request.url.endsWith('/video-games'));
    search(fixture, 'had');
    searchRequest(httpMock, 'had').flush([ownedGame]);
    search(fixture, ' had ');
    httpMock.expectNone(request => request.url.endsWith('/library'));
    search(fixture, ' \t ');
    listRequest(httpMock).flush([ownedGame]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('1 video game');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('found');
  });

  it.each(['success', '503', '401'])('ignores an older search %s after a newer search completes', outcome => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    search(fixture, 'old');
    const old = searchRequest(httpMock, 'old');
    search(fixture, 'new');
    searchRequest(httpMock, 'new').flush([{ ...ownedGame, name: 'New title' }]);
    if (outcome === 'success') old.flush([{ ...ownedGame, name: 'Old title' }]);
    else old.flush({}, { status: Number(outcome), statusText: 'Old error' });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('New title');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Old title');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Unable to load');
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
  });

  it('ignores initial/search responses after clearing, including a late empty result', () => {
    flushSupporting(httpMock);
    const initial = listRequest(httpMock);
    search(fixture, 'missing');
    const pending = searchRequest(httpMock, 'missing');
    buttonByText(fixture, 'Clear search').click();
    listRequest(httpMock).flush([ownedGame]);
    initial.flush([{ ...ownedGame, name: 'Obsolete' }]);
    pending.flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Obsolete');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('No video games match');
  });

  it('keeps search usable on failure and retries the same query', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    search(fixture, 'had');
    searchRequest(httpMock, 'had').flush({}, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(searchInput(fixture).disabled).toBe(false);
    buttonByText(fixture, 'Try again').click();
    searchRequest(httpMock, 'had').flush([ownedGame]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
  });

  it('edits a searched result and refreshes with the same search', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    search(fixture, 'had');
    searchRequest(httpMock, 'had').flush([ownedGame]);
    fixture.detectChanges();
    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();
    flushSupporting(httpMock);
    setName(fixture, 'New name');
    buttonByText(fixture, 'Save').click();
    httpMock.expectOne(request => request.method === 'PUT' && request.url.endsWith('/video-games/1'))
      .flush({ ...ownedGame, name: 'New name' });
    searchRequest(httpMock, 'had').flush([]);
    fixture.detectChanges();
    expect(searchInput(fixture).value).toBe('had');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No video games match');
  });

  it('keeps cover-conflict reconciliation unfiltered when a searched game has been renamed', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    search(fixture, 'had');
    searchRequest(httpMock, 'had').flush([ownedGame]);
    fixture.componentInstance.openEdit(ownedGame);
    fixture.detectChanges();
    flushSupporting(httpMock);
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'remove' });
    fixture.componentInstance.onSubmit(ownedGame);
    httpMock.expectOne(request => request.method === 'PUT' && request.url.endsWith('/games/1/cover'))
      .flush({}, { status: 409, statusText: 'Cover changed' });
    listRequest(httpMock).flush([{ ...ownedGame, name: 'Renamed elsewhere', coverUploadId: 'new-cover' }]);
    expect(save.phase()).toBe('conflict');
    expect(save.currentCover()?.coverUploadId).toBe('new-cover');
    expect(searchInput(fixture).value).toBe('had');
    httpMock.expectNone(request => request.url.endsWith('/library'));
  });

  it('resets page-local search and ignores a pending result across an account generation change', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    search(fixture, 'had');
    const old = searchRequest(httpMock, 'had');
    emitAuth('SIGNED_OUT', null);
    emitAuth('SIGNED_IN', createTestSession());
    old.flush([ownedGame]);
    fixture.detectChanges();
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.detectChanges();
    expect(searchInput(fixture).value).toBe('');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Hades');
  });

  it('cancels a pending search read when leaving the page', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    search(fixture, 'had');
    const pending = searchRequest(httpMock, 'had');
    fixture.destroy();
    expect(pending.cancelled).toBe(true);
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

  it('renders the positive Family friendly chip only on classified games', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame, { ...ownedGame, id: '2', name: 'Family game', isFamilyFriendly: true }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cards = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.video-games__item')];
    expect(cards[0].querySelector('.family-friendly-chip')).toBeNull();
    expect(cards[1].querySelector('.family-friendly-chip')?.textContent).toBe('Family friendly');
    expect(cards.some((card) => card.textContent?.includes('Not family friendly'))).toBe(false);
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
      coverUpdate: { intent: 'keep' },
      isFamilyFriendly: false,
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

  it('uses cover-only CAS for an explicit cover choice with normalized unchanged metadata', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    fixture.detectChanges();
    fixture.componentInstance.openEdit(ownedGame);
    fixture.detectChanges();
    flushSupporting(httpMock);
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'remove' });
    fixture.detectChanges();
    setName(fixture, '  Hades  ');
    buttonByText(fixture, 'Save').click();
    const request = httpMock.expectOne((req) => req.url.endsWith('/games/1/cover'));
    expect(request.request.body).toEqual({ coverUpdate: {
      intent: 'remove', expectedCover: { coverImageUrl: null, coverUploadId: null },
    } });
    httpMock.expectNone((req) => req.method === 'PUT' && req.url.endsWith('/video-games/1'));
    request.flush({ gameId: '1', coverImageUrl: null, coverUploadId: null });
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
  });

  it('uses a combined update for metadata plus cover and retains edits across a 409 user choice', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([ownedGame]);
    fixture.componentInstance.openEdit(ownedGame);
    fixture.detectChanges();
    flushSupporting(httpMock);
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'external', url: 'https://example.test/pending.jpg' });
    fixture.detectChanges();
    setName(fixture, 'Unsaved rename');
    buttonByText(fixture, 'Save').click();
    httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/video-games/1'))
      .flush({}, { status: 409, statusText: 'Cover changed' });
    listRequest(httpMock).flush([{ ...ownedGame, name: 'Do not reset form', coverImageUrl: 'https://example.test/newer.jpg' }]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#game-form-name')?.value).toBe('Unsaved rename');
    expect(save.draft()).toEqual({ intent: 'external', url: 'https://example.test/pending.jpg' });
    save.chooseConflict(true);
    fixture.detectChanges();
    buttonByText(fixture, 'Save').click();
    const retry = httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/video-games/1'));
    expect(retry.request.body.name).toBe('Unsaved rename');
    expect(retry.request.body.coverUpdate.expectedCover).toEqual({ coverImageUrl: 'https://example.test/newer.jpg', coverUploadId: null });
    retry.flush(ownedGame);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
  });

  it('sends keep for metadata-only edits instead of restoring the old managed cover', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openEdit({ ...ownedGame, coverUploadId: 'managed' });
    const save = fixture.debugElement.injector.get(CoverSave);
    const input: VideoGameInput = { ...ownedGame, name: 'Rename', coverUpdate: { intent: 'keep' } };
    fixture.componentInstance.onSubmit(input);
    const request = httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/video-games/1'));
    expect(request.request.body.coverUpdate).toEqual({ intent: 'keep' });
    expect(request.request.body).not.toHaveProperty('coverImageUrl');
    expect(request.request.body).not.toHaveProperty('coverUploadId');
    request.flush({ ...ownedGame, coverUploadId: 'newer' });
    listRequest(httpMock).flush([]);
    expect(save.phase()).toBe('idle');
    await fixture.whenStable();
  });

  it('stages only on submit, blocks duplicates, and Close/Escape before completion prevents Create', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    httpMock.expectNone((req) => req.method === 'POST');
    fixture.componentInstance.onSubmit(ownedGame);
    fixture.componentInstance.onSubmit(ownedGame);
    const upload = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads'));
    fixture.componentInstance.closeForm();
    expect(upload.cancelled).toBe(true);
    httpMock.expectNone((req) => req.method === 'POST');
    expect(save.draft()).toEqual({ intent: 'keep' });
  });

  it.each(['escape', 'cancel', 'backdrop', 'destroy'])('%s invalidates a delayed upload before any Game write', (dismissal) => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    flushSupporting(httpMock);
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    fixture.componentInstance.onSubmit(ownedGame);
    const upload = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads'));
    const dialog = (fixture.nativeElement as HTMLElement).querySelector('dialog')!;
    if (dismissal === 'destroy') fixture.destroy();
    else if (dismissal === 'escape') dialog.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    else if (dismissal === 'cancel') dialog.dispatchEvent(new Event('cancel', { cancelable: true }));
    else dialog.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    expect(upload.cancelled).toBe(true);
    expect(save.draft()).toEqual({ intent: 'keep' });
    httpMock.expectNone((req) => req.method === 'POST');
  });

  it('keeps ambiguous Create recovery outside a closed form and reuses the same handle only after explicit status/retry', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    fixture.componentInstance.onSubmit(ownedGame);
    const ready = { id: 'upload', state: 'Ready', gameId: null, gameExists: null, expiresAt: new Date(Date.now() + 60_000).toISOString() };
    httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads')).flush(ready);
    const create = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/video-games'));
    expect(create.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    fixture.componentInstance.closeForm();
    expect(create.cancelled).toBe(true);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Closing cannot undo');
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-video-game-form')).toBeNull();
    buttonByText(fixture, 'Check save status').click();
    httpMock.expectOne((req) => req.method === 'GET' && req.url.endsWith('/cover-uploads/upload')).flush(ready);
    httpMock.expectNone((req) => req.method === 'POST');
    fixture.detectChanges();
    buttonByText(fixture, 'Retry same save').click();
    const retry = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/video-games'));
    expect(retry.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    retry.flush(ownedGame);
    listRequest(httpMock).flush([ownedGame]);
    await fixture.whenStable();
  });

  it.each(['success', '401'])('ignores an old list %s after batched same-user logout/login', (outcome) => {
    flushSupporting(httpMock);
    const oldList = listRequest(httpMock);
    emitAuth('SIGNED_OUT', null);
    emitAuth('SIGNED_IN', createTestSession());
    if (outcome === 'success') oldList.flush([ownedGame]);
    else oldList.flush({}, { status: 401, statusText: 'Old session' });
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    fixture.detectChanges();
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Hades');
    expect(TestBed.inject(Router).url).not.toBe('/login');
  });

  it.each(['success', '401'])('ignores an old Game-write %s before same-user login effects run', (outcome) => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.componentInstance.onSubmit(ownedGame);
    const oldWrite = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/video-games'));
    const close = vi.spyOn(fixture.componentInstance, 'closeForm');
    emitAuth('SIGNED_OUT', null);
    emitAuth('SIGNED_IN', createTestSession());
    if (outcome === 'success') oldWrite.flush(ownedGame);
    else oldWrite.flush({}, { status: 401, statusText: 'Old session' });
    expect(close).not.toHaveBeenCalled();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
    fixture.detectChanges();
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    flushSupporting(httpMock);
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#game-form-name')?.value).toBe('');
    httpMock.expectNone((req) => req.method === 'POST');
  });

  it('releases a visible local draft on Close and never restores it when opening a fresh form', () => {
    const createUrl = vi.fn<(blob: Blob | MediaSource) => string>(() => 'blob:local-draft');
    const revokeUrl = vi.fn<(url: string) => void>();
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = createUrl;
      static override revokeObjectURL = revokeUrl;
    });
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    flushSupporting(httpMock);
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['private local image']) });
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('img[src="blob:local-draft"]')).not.toBeNull();
    fixture.componentInstance.closeForm();
    fixture.detectChanges();
    expect(revokeUrl).toHaveBeenCalledWith('blob:local-draft');
    expect(save.draft()).toEqual({ intent: 'keep' });
    expect((fixture.nativeElement as HTMLElement).querySelector('img[src="blob:local-draft"]')).toBeNull();
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    flushSupporting(httpMock);
    expect(createUrl).toHaveBeenCalledOnce();
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
  });

  it('keeps external/no-handle unknown saves blocked outside the form, with explicit Library inspection and no retry/Create', () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'external', url: 'https://example.test/a.jpg' });
    fixture.componentInstance.onSubmit(ownedGame);
    httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/video-games'))
      .flush({}, { status: 503, statusText: 'Unknown' });
    fixture.componentInstance.closeForm();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/library"]')?.textContent).toBe('Inspect Library');
    fixture.componentInstance.openCreate();
    buttonByText(fixture, 'Check save status').click();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-video-game-form')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No upload receipt');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Retry same save');
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE' || req.url.includes('/cover-uploads/'));
  });

  it('does not reopen an empty dialog from a queued open after immediate Close', async () => {
    flushSupporting(httpMock);
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.componentInstance.closeForm();
    await Promise.resolve();
    expect((fixture.nativeElement as HTMLElement).querySelector('dialog')?.hasAttribute('open')).toBe(false);
    httpMock.expectNone((req) => req.method === 'POST');
  });
});
