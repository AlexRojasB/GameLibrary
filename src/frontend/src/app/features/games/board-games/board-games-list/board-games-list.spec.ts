import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../../core/auth/testing/supabase-client.mock';

import { BoardGame, BoardGameInput } from '../../board-game';
import { CoverSave } from '../../covers/cover-save';

import { BoardGamesList } from './board-games-list';

const catan: BoardGame = {
  id: '1',
  name: 'Catan',
  coverImageUrl: null,
  coverUploadId: null,
  isFamilyFriendly: true,
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
  coverUploadId: null,
  isFamilyFriendly: false,
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

async function configure(): Promise<{ fixture: ComponentFixture<BoardGamesList>; httpMock: HttpTestingController; emitAuth: ReturnType<typeof createMockSupabase>['emit'] }> {
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
  await TestBed.inject(AuthService).initialize();
  const fixture = TestBed.createComponent(BoardGamesList);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock, emitAuth: mock.emit };
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

  it('renders the positive Family friendly chip only on classified games', async () => {
    listRequest(httpMock).flush([catan, soloGame]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cards = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.board-games__item')];
    expect(cards[0].querySelector('.family-friendly-chip')?.textContent).toBe('Family friendly');
    expect(cards[1].querySelector('.family-friendly-chip')).toBeNull();
    expect(cards.some((card) => card.textContent?.includes('Not family friendly'))).toBe(false);
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
      isFamilyFriendly: false,
      minimumPlayers: 3,
      maximumPlayers: 4,
      approximateDuration: null,
      interactionType: 'Competitive',
      acquisitionStatus: 'Owned',
      rating: null,
      notes: null,
      coverUpdate: { intent: 'keep' },
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

  it('uses cover-only CAS for an explicit cover choice with normalized unchanged metadata', async () => {
    listRequest(httpMock).flush([catan]);
    fixture.detectChanges();
    fixture.componentInstance.openEdit(catan);
    fixture.detectChanges();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'remove' });
    fixture.detectChanges();
    setName(fixture, '  Catan  ');
    buttonByText(fixture, 'Save').click();
    const request = httpMock.expectOne((req) => req.url.endsWith('/games/1/cover'));
    expect(request.request.body).toEqual({ coverUpdate: {
      intent: 'remove', expectedCover: { coverImageUrl: null, coverUploadId: null },
    } });
    httpMock.expectNone((req) => req.method === 'PUT' && req.url.endsWith('/board-games/1'));
    request.flush({ gameId: '1', coverImageUrl: null, coverUploadId: null });
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
  });

  it('uses a combined update for metadata plus cover and retains edits across a 409 user choice', async () => {
    listRequest(httpMock).flush([catan]);
    fixture.componentInstance.openEdit(catan);
    fixture.detectChanges();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'external', url: 'https://example.test/pending.jpg' });
    fixture.detectChanges();
    setName(fixture, 'Unsaved rename');
    buttonByText(fixture, 'Save').click();
    httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/board-games/1'))
      .flush({}, { status: 409, statusText: 'Cover changed' });
    listRequest(httpMock).flush([{ ...catan, name: 'Do not reset form', coverImageUrl: 'https://example.test/newer.jpg' }]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#board-game-form-name')?.value).toBe('Unsaved rename');
    expect(save.draft()).toEqual({ intent: 'external', url: 'https://example.test/pending.jpg' });
    save.chooseConflict(true);
    fixture.detectChanges();
    buttonByText(fixture, 'Save').click();
    const retry = httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/board-games/1'));
    expect(retry.request.body.name).toBe('Unsaved rename');
    expect(retry.request.body.coverUpdate.expectedCover).toEqual({ coverImageUrl: 'https://example.test/newer.jpg', coverUploadId: null });
    retry.flush(catan);
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
  });

  it('sends keep for metadata-only edits instead of restoring the old managed cover', async () => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openEdit({ ...catan, coverUploadId: 'managed' });
    const save = fixture.debugElement.injector.get(CoverSave);
    const input: BoardGameInput = { ...catan, name: 'Rename', coverUpdate: { intent: 'keep' } };
    fixture.componentInstance.onSubmit(input);
    const request = httpMock.expectOne((req) => req.method === 'PUT' && req.url.endsWith('/board-games/1'));
    expect(request.request.body.coverUpdate).toEqual({ intent: 'keep' });
    expect(request.request.body).not.toHaveProperty('coverImageUrl');
    expect(request.request.body).not.toHaveProperty('coverUploadId');
    request.flush({ ...catan, coverUploadId: 'newer' });
    listRequest(httpMock).flush([]);
    expect(save.phase()).toBe('idle');
    await fixture.whenStable();
  });

  it('stages only on submit, blocks duplicates, and Close/Escape before completion prevents Create', () => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    httpMock.expectNone((req) => req.method === 'POST');
    fixture.componentInstance.onSubmit(catan);
    fixture.componentInstance.onSubmit(catan);
    const upload = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads'));
    fixture.componentInstance.closeForm();
    expect(upload.cancelled).toBe(true);
    httpMock.expectNone((req) => req.method === 'POST');
    expect(save.draft()).toEqual({ intent: 'keep' });
  });

  it.each(['escape', 'cancel', 'backdrop', 'destroy'])('%s invalidates a delayed upload before any Game write', (dismissal) => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    fixture.componentInstance.onSubmit(catan);
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
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'local', blob: new Blob(['image']) });
    fixture.componentInstance.onSubmit(catan);
    const ready = { id: 'upload', state: 'Ready', gameId: null, gameExists: null, expiresAt: new Date(Date.now() + 60_000).toISOString() };
    httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads')).flush(ready);
    const create = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/board-games'));
    expect(create.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    fixture.componentInstance.closeForm();
    expect(create.cancelled).toBe(true);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Closing cannot undo');
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-board-game-form')).toBeNull();
    buttonByText(fixture, 'Check save status').click();
    httpMock.expectOne((req) => req.method === 'GET' && req.url.endsWith('/cover-uploads/upload')).flush(ready);
    httpMock.expectNone((req) => req.method === 'POST');
    fixture.detectChanges();
    buttonByText(fixture, 'Retry same save').click();
    const retry = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/board-games'));
    expect(retry.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    retry.flush(catan);
    listRequest(httpMock).flush([catan]);
    await fixture.whenStable();
  });

  it.each(['success', '401'])('ignores an old list %s after batched same-user logout/login', (outcome) => {
    const oldList = listRequest(httpMock);
    emitAuth('SIGNED_OUT', null);
    emitAuth('SIGNED_IN', createTestSession());
    if (outcome === 'success') oldList.flush([catan]);
    else oldList.flush({}, { status: 401, statusText: 'Old session' });
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    fixture.detectChanges();
    listRequest(httpMock).flush([]);
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Catan');
    expect(TestBed.inject(Router).url).not.toBe('/login');
  });

  it.each(['success', '401'])('ignores an old Game-write %s before same-user login effects run', (outcome) => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.componentInstance.onSubmit(catan);
    const oldWrite = httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/board-games'));
    const close = vi.spyOn(fixture.componentInstance, 'closeForm');
    emitAuth('SIGNED_OUT', null);
    emitAuth('SIGNED_IN', createTestSession());
    if (outcome === 'success') oldWrite.flush(catan);
    else oldWrite.flush({}, { status: 401, statusText: 'Old session' });
    expect(close).not.toHaveBeenCalled();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
    fixture.detectChanges();
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#board-game-form-name')?.value).toBe('');
    httpMock.expectNone((req) => req.method === 'POST');
  });

  it('releases a visible local draft on Close and never restores it when opening a fresh form', () => {
    const createUrl = vi.fn<(blob: Blob | MediaSource) => string>(() => 'blob:local-draft');
    const revokeUrl = vi.fn<(url: string) => void>();
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = createUrl;
      static override revokeObjectURL = revokeUrl;
    });
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.detectChanges();
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
    expect(createUrl).toHaveBeenCalledOnce();
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
  });

  it('keeps external/no-handle unknown saves blocked outside the form, with explicit Library inspection and no retry/Create', () => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    const save = fixture.debugElement.injector.get(CoverSave);
    save.setDraft({ intent: 'external', url: 'https://example.test/a.jpg' });
    fixture.componentInstance.onSubmit(catan);
    httpMock.expectOne((req) => req.method === 'POST' && req.url.endsWith('/board-games'))
      .flush({}, { status: 503, statusText: 'Unknown' });
    fixture.componentInstance.closeForm();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('a[href="/library"]')?.textContent).toBe('Inspect Library');
    fixture.componentInstance.openCreate();
    buttonByText(fixture, 'Check save status').click();
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).querySelector('app-board-game-form')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No upload receipt');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Retry same save');
    httpMock.expectNone((req) => req.method === 'POST' || req.method === 'DELETE' || req.url.includes('/cover-uploads/'));
  });

  it('does not reopen an empty dialog from a queued open after immediate Close', async () => {
    listRequest(httpMock).flush([]);
    fixture.componentInstance.openCreate();
    fixture.componentInstance.closeForm();
    await Promise.resolve();
    expect((fixture.nativeElement as HTMLElement).querySelector('dialog')?.hasAttribute('open')).toBe(false);
    httpMock.expectNone((req) => req.method === 'POST');
  });
});
