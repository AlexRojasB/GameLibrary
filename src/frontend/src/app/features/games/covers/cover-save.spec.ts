import { HttpClient, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { AuthService } from '../../../core/auth/auth.service';
import { createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { CoverSave, coverUpdateFor, metadataChanged } from './cover-save';
import { CoverReference, CoverUpdate, CoverUploadStatus } from './cover-types';

const empty: CoverReference = { coverImageUrl: null, coverUploadId: null };
const managed: CoverReference = { coverImageUrl: null, coverUploadId: 'old' };
const ready = (): CoverUploadStatus => ({
  id: 'upload', state: 'Ready', gameId: null, gameExists: null,
  expiresAt: new Date(Date.now() + 60_000).toISOString(),
});

describe('cover intent serialization', () => {
  it('never infers remove from an empty managed URL or explicit equality', () => {
    expect(coverUpdateFor({ intent: 'keep' }, managed)).toEqual({ intent: 'keep' });
    const saved = { coverImageUrl: 'https://example.test/a.jpg', coverUploadId: null, name: 'Not a cover field' };
    expect(coverUpdateFor({ intent: 'external', url: saved.coverImageUrl }, saved)).toEqual({
      intent: 'external', url: saved.coverImageUrl,
      expectedCover: { coverImageUrl: saved.coverImageUrl, coverUploadId: null },
    });
    expect(coverUpdateFor({ intent: 'remove' }, managed)).toEqual({ intent: 'remove', expectedCover: managed });
  });

  it('omits Create snapshots, but includes both null references for an explicit Edit', () => {
    expect(coverUpdateFor({ intent: 'remove' }, null)).toEqual({ intent: 'remove' });
    expect(coverUpdateFor({ intent: 'external', url: ' https://example.test/a.jpg ' }, null)).toEqual({
      intent: 'external', url: 'https://example.test/a.jpg',
    });
    expect(coverUpdateFor({ intent: 'local', blob: new Blob(['image']) }, empty, 'upload')).toEqual({
      intent: 'upload', uploadId: 'upload', expectedCover: empty,
    });
  });

  it('normalizes metadata without using cover equality to choose intent', () => {
    expect(metadataChanged({ name: ' Game ', notes: '  ', platformIds: ['b', 'a'], coverUpdate: { intent: 'remove' } },
      { name: 'Game', notes: null, platformIds: ['a', 'b'], ...managed })).toBe(false);
    expect(metadataChanged({ name: 'Game', isFamilyFriendly: true }, { name: 'Game', isFamilyFriendly: false })).toBe(true);
  });
});

describe('CoverSave lifecycle', () => {
  let save: CoverSave;
  let http: HttpClient;
  let requests: HttpTestingController;
  let session = signal<ReturnType<AuthService['session']>>(null);
  let saved = vi.fn<() => void>();
  let unauthorized = vi.fn<() => void>();
  let missing = vi.fn<() => void>();

  function job(gameId: string | null = null) {
    return {
      gameId,
      write: (coverUpdate: CoverUpdate) => http.post('/write', { name: 'Submitted metadata', coverUpdate }),
      refetch: (id: string) => http.get<CoverReference | null>(`/original/${id}`),
      saved, unauthorized, missing,
    };
  }
  function uploadRequest() { return requests.expectOne((req) => req.method === 'POST' && req.url.endsWith('/cover-uploads')); }
  function statusRequest() { return requests.expectOne((req) => req.method === 'GET' && req.url.endsWith('/cover-uploads/upload')); }
  function beginLocal(game: CoverReference | null = null): void {
    expect(save.begin(game)).toBe(true);
    save.setDraft({ intent: 'local', blob: new Blob(['local'], { type: 'image/jpeg' }) });
  }
  function knownFailure(gameId: string | null = null): void {
    save.save(job(gameId));
    uploadRequest().flush(ready());
    requests.expectOne('/write').flush({ detail: 'Invalid metadata' }, { status: 400, statusText: 'Bad Request' });
  }
  function unknownFailure(): void {
    beginLocal();
    save.save(job());
    uploadRequest().flush(ready());
    requests.expectOne('/write').flush({}, { status: 503, statusText: 'Unknown' });
    expect(save.phase()).toBe('unknown');
  }
  function newSession(id = 'a') {
    const value = createTestSession();
    return { ...value, user: { ...value.user, id } };
  }
  function loginAgain(): void {
    session.set(null);
    session.set(newSession());
  }

  beforeEach(() => {
    session = signal<ReturnType<AuthService['session']>>(newSession());
    TestBed.configureTestingModule({ providers: [
      CoverSave, provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { session } },
    ] });
    save = TestBed.inject(CoverSave);
    http = TestBed.inject(HttpClient);
    requests = TestBed.inject(HttpTestingController);
    saved = vi.fn(); unauthorized = vi.fn(); missing = vi.fn();
    TestBed.tick();
  });

  afterEach(() => {
    save.close();
    requests.match((req) => req.method === 'DELETE').forEach((req) => { if (!req.cancelled) req.flush(null); });
    requests.verify();
    vi.restoreAllMocks();
  });

  it('uploads only after valid final submit, then writes once with the returned handle', () => {
    beginLocal();
    requests.expectNone((req) => req.method === 'POST');
    save.save(job());
    save.save(job());
    expect(save.phase()).toBe('uploading');
    const upload = uploadRequest();
    expect(upload.request.body.get('file')).toBeInstanceOf(Blob);
    requests.expectNone('/write');
    save.setDraft({ intent: 'remove' });
    expect(save.draft().intent).toBe('local');
    upload.flush(ready());
    expect(save.phase()).toBe('saving');
    const write = requests.expectOne('/write');
    expect(write.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    write.flush({});
  });

  it('retains a live Ready handle and local image after a known validation failure', () => {
    beginLocal(managed);
    knownFailure('game');
    expect(save.phase()).toBe('idle');
    expect(save.draft().intent).toBe('local');
    save.save(job('game'));
    requests.expectNone((req) => req.url.endsWith('/cover-uploads') && req.method === 'POST');
    const retry = requests.expectOne('/write');
    expect(retry.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload', expectedCover: managed });
    retry.flush({});
    expect(saved).toHaveBeenCalledOnce();
    requests.expectNone((req) => req.method === 'DELETE');
  });

  it('preserves uncertain-save recovery and its same handle across a normal Supabase token refresh', () => {
    const token = (revision: number) => `header.${btoa(JSON.stringify({ session_id: '11111111-1111-4111-8111-111111111111', iat: revision }))}.signature`;
    session.set(createTestSession({ access_token: token(1) }));
    TestBed.tick();
    unknownFailure();
    const originalAccount = save.account();
    session.set(createTestSession({ access_token: token(2), refresh_token: 'rotated-refresh-token' }));
    TestBed.tick();
    expect(save.account()).toBe(originalAccount);
    expect(save.phase()).toBe('unknown');
    expect(save.draft().intent).toBe('local');
    expect(save.begin(null)).toBe(false);
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'Attached', gameId: 'original-game', gameExists: true, expiresAt: null });
    requests.expectOne('/original/original-game').flush(managed);
    expect(saved).toHaveBeenCalledOnce();
    requests.expectNone('/write');
  });

  it('requires explicit re-upload after expiry, never uploads automatically', () => {
    beginLocal();
    knownFailure();
    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 120_000);
    save.save(job());
    expect(save.phase()).toBe('expired');
    requests.expectNone('/write');
    save.reupload();
    requests.expectOne((req) => req.method === 'DELETE').flush(null);
    requests.expectNone((req) => req.method === 'POST');
    save.save(job());
    uploadRequest().flush(ready());
    requests.expectOne('/write').flush({});
  });

  it('source replacement cancels only the unclaimed staged handle, never the saved cover', () => {
    beginLocal(managed);
    knownFailure('game');
    save.setDraft({ intent: 'external', url: 'https://example.test/new.jpg' });
    const cancellation = requests.expectOne((req) => req.method === 'DELETE');
    expect(cancellation.request.url).toMatch(/\/cover-uploads\/upload$/);
    cancellation.flush(null);
    save.save(job('game'));
    const write = requests.expectOne('/write');
    expect(write.request.body.coverUpdate).toEqual({ intent: 'external', url: 'https://example.test/new.jpg', expectedCover: managed });
    write.flush({});
  });

  it('Close aborts a delayed upload and invalidates it before any Game write', () => {
    beginLocal();
    save.save(job());
    const upload = uploadRequest();
    save.close();
    expect(upload.cancelled).toBe(true);
    expect(save.draft()).toEqual({ intent: 'keep' });
    requests.expectNone('/write');
    expect(save.begin(empty)).toBe(true);
    expect(saved).not.toHaveBeenCalled();
  });

  it('Close after write dispatch retains unknown recovery outside the form and forbids new Create', () => {
    beginLocal();
    save.save(job());
    uploadRequest().flush(ready());
    const write = requests.expectOne('/write');
    save.close();
    expect(write.cancelled).toBe(true);
    expect(save.phase()).toBe('unknown');
    expect(save.message()).toContain('Closing cannot undo');
    expect(save.begin(null)).toBe(false);
    requests.expectNone((req) => req.method === 'DELETE');
    save.reconcile();
    statusRequest().flush(ready());
    expect(save.phase()).toBe('retry');
    requests.expectNone('/write');
    save.retrySameHandle();
    const retry = requests.expectOne('/write');
    expect(retry.request.body.coverUpdate).toEqual({ intent: 'upload', uploadId: 'upload' });
    retry.flush({});
  });

  it.each([0, 500, 503])('never retries an unexplained %s write or deletes its possibly attached object', (status) => {
    beginLocal();
    save.save(job());
    uploadRequest().flush(ready());
    const write = requests.expectOne('/write');
    if (status === 0) write.error(new ProgressEvent('error'));
    else write.flush({}, { status, statusText: 'Unknown outcome' });
    save.save(job());
    save.setDraft({ intent: 'remove' });
    save.reupload();
    expect(save.phase()).toBe('unknown');
    requests.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
  });

  it('reconciles a retired first-claim receipt by refetching the ORIGINAL game, never re-Create', () => {
    unknownFailure();
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'DeletePending', gameId: 'original', gameExists: true, expiresAt: null });
    requests.expectOne('/original/original').flush({ ...empty, name: 'Changed later' });
    expect(saved).toHaveBeenCalledOnce();
    expect(save.message()).toContain('Current fields may have changed');
    requests.expectNone('/write');
    requests.expectNone((req) => req.method === 'DELETE');
  });

  it.each(['purged', 'deleted', 'unavailable'])('keeps %s recovery blocked and offers Library inspection', (outcome) => {
    unknownFailure();
    save.reconcile();
    const status = statusRequest();
    if (outcome === 'deleted') status.flush({ ...ready(), state: 'DeletePending', gameId: 'original', gameExists: false });
    else status.flush({}, { status: outcome === 'purged' ? 404 : 503, statusText: outcome });
    expect(save.phase()).toBe('unknown');
    expect(save.message()).toContain('Library');
    expect(save.begin(null)).toBe(false);
    requests.expectNone('/write');
    requests.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
  });

  it('requires Library inspection for an unknown save without an upload handle', () => {
    save.begin(empty);
    save.setDraft({ intent: 'remove' });
    save.save(job('game'));
    requests.expectOne('/write').error(new ProgressEvent('error'));
    save.reconcile();
    expect(save.phase()).toBe('unknown');
    expect(save.message()).toContain('No upload receipt');
    requests.expectNone((req) => req.method === 'GET');
  });

  it('refetches a 409 current cover but retains the draft and waits for a user choice', () => {
    save.begin(managed);
    save.setDraft({ intent: 'external', url: 'https://example.test/pending.jpg' });
    save.save(job('game'));
    requests.expectOne('/write').flush({}, { status: 409, statusText: 'Cover changed' });
    requests.expectOne('/original/game').flush({ coverImageUrl: 'https://example.test/newer.jpg', coverUploadId: null });
    expect(save.phase()).toBe('conflict');
    expect(save.savedCover()).toEqual(managed);
    expect(save.draft().intent).toBe('external');
    requests.expectNone('/write');
    save.chooseConflict(true);
    save.save(job('game'));
    const write = requests.expectOne('/write');
    expect(write.request.body.coverUpdate.expectedCover).toEqual({ coverImageUrl: 'https://example.test/newer.jpg', coverUploadId: null });
    write.flush({});
  });

  it('offers explicit re-upload for a 409 expired handle after authoritative status', () => {
    beginLocal(managed);
    save.save(job('game'));
    uploadRequest().flush(ready());
    requests.expectOne('/write').flush({}, { status: 409, statusText: 'Expired' });
    statusRequest().flush({ ...ready(), expiresAt: new Date(0).toISOString() });
    expect(save.phase()).toBe('expired');
    expect(save.draft().intent).toBe('local');
    requests.expectNone((req) => req.method === 'POST');
  });

  it('Close during known 409 review cancels only its unclaimed handle rather than assuming an unknown commit', () => {
    beginLocal(managed);
    save.save(job('game'));
    uploadRequest().flush(ready());
    requests.expectOne('/write').flush({}, { status: 409, statusText: 'Cover changed' });
    const status = statusRequest();
    save.close();
    expect(status.cancelled).toBe(true);
    requests.expectOne((req) => req.method === 'DELETE').flush(null);
    expect(save.phase()).toBe('idle');
    expect(save.begin(null)).toBe(true);
  });

  it('reconciles cancellation 409 rather than forcing deletion or assuming rollback', () => {
    beginLocal(managed);
    knownFailure('game');
    save.close();
    requests.expectOne((req) => req.method === 'DELETE').flush({}, { status: 409, statusText: 'Attached' });
    expect(save.phase()).toBe('unknown');
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'Attached', gameId: 'original', gameExists: true });
    requests.expectOne('/original/original').flush(empty);
    expect(saved).toHaveBeenCalledOnce();
    requests.expectNone((req) => req.method === 'DELETE');
  });

  it('preserves a pending cancellation across Close so its 409 cannot be mistaken for a fresh Create opportunity', () => {
    beginLocal(managed);
    knownFailure('game');
    save.setDraft({ intent: 'remove' });
    const cancel = requests.expectOne((req) => req.method === 'DELETE');
    save.close();
    expect(save.begin(null)).toBe(false);
    cancel.flush({}, { status: 409, statusText: 'Attached' });
    expect(save.phase()).toBe('unknown');
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'DeletePending', gameId: 'original', gameExists: true });
    requests.expectOne('/original/original').flush(empty);
    expect(saved).toHaveBeenCalledOnce();
  });

  it('gives actionable stale-PWA refresh feedback without discarding the draft', () => {
    save.begin(managed);
    save.setDraft({ intent: 'remove' });
    save.save(job('game'));
    requests.expectOne('/write').flush({ code: 'cover_intent_required' }, { status: 400, statusText: 'Bad Request' });
    expect(save.message()).toContain('Refresh or update the PWA');
    expect(save.draft()).toEqual({ intent: 'remove' });
  });

  it('ignores stale-account upload success and 401 even before the account effect runs', () => {
    beginLocal();
    save.save(job());
    session.set(newSession('b'));
    uploadRequest().flush(ready());
    requests.expectNone('/write');
    expect(unauthorized).not.toHaveBeenCalled();
    TestBed.tick();
    expect(save.draft()).toEqual({ intent: 'keep' });
    expect(save.phase()).toBe('idle');
    save.begin(empty);
    save.save(job('game'));
    session.set(newSession('c'));
    requests.expectOne('/write').flush({}, { status: 401, statusText: 'Unauthorized' });
    expect(unauthorized).not.toHaveBeenCalled();
    expect(saved).not.toHaveBeenCalled();
    TestBed.tick();
  });

  it.each(['success', '401'])('ignores an old upload %s after batched logout/login to the SAME user, before effects run', (outcome) => {
    beginLocal();
    save.save(job());
    const upload = uploadRequest();
    loginAgain();
    if (outcome === 'success') upload.flush(ready());
    else upload.flush({}, { status: 401, statusText: 'Old session' });
    requests.expectNone('/write');
    expect(unauthorized).not.toHaveBeenCalled();
    TestBed.tick();
    expect(save.phase()).toBe('idle');
    expect(save.draft()).toEqual({ intent: 'keep' });
  });

  it.each(['success', '401', '503'])('ignores an old Game-write %s after same-user login and does not close/recover a newer form', (outcome) => {
    beginLocal();
    save.save(job());
    uploadRequest().flush(ready());
    const write = requests.expectOne('/write');
    loginAgain();
    if (outcome === 'success') write.flush({});
    else write.flush({}, { status: Number(outcome), statusText: 'Old session' });
    expect(saved).not.toHaveBeenCalled();
    expect(unauthorized).not.toHaveBeenCalled();
    TestBed.tick();
    expect(save.begin(empty)).toBe(true);
    expect(save.phase()).toBe('idle');
    expect(save.message()).toBe('');
    requests.expectNone((req) => req.method === 'DELETE' || req.method === 'POST');
  });

  it('invalidates synchronously before a stale form can submit or cancel a Ready handle using the new session', () => {
    beginLocal();
    knownFailure();
    loginAgain();
    save.save(job());
    save.close();
    requests.expectNone((req) => req.method === 'POST' || req.method === 'DELETE');
    expect(save.draft()).toEqual({ intent: 'keep' });
    expect(save.savedCover()).toBeNull();
    TestBed.tick();
  });

  it.each(['status', 'refetch'])('does not accept old %s recovery evidence after same-user login', (step) => {
    unknownFailure();
    save.reconcile();
    const status = statusRequest();
    if (step === 'status') {
      loginAgain();
      status.flush(ready());
    } else {
      status.flush({ ...ready(), gameId: 'original', gameExists: true, state: 'DeletePending' });
      const refetch = requests.expectOne('/original/original');
      loginAgain();
      refetch.flush(empty);
    }
    expect(saved).not.toHaveBeenCalled();
    expect(unauthorized).not.toHaveBeenCalled();
    TestBed.tick();
    expect(save.phase()).toBe('idle');
    requests.expectNone('/write');
  });

  it.each([401, 409])('ignores old cancellation %s after same-user login, even before effects run', (status) => {
    beginLocal();
    knownFailure();
    save.close();
    const cancellation = requests.expectOne((req) => req.method === 'DELETE');
    loginAgain();
    cancellation.flush({}, { status, statusText: 'Old session' });
    expect(unauthorized).not.toHaveBeenCalled();
    TestBed.tick();
    expect(save.begin(empty)).toBe(true);
    expect(save.phase()).toBe('idle');
    expect(save.message()).toBe('');
    requests.expectNone((req) => req.method === 'GET' || req.method === 'POST');
  });

  it('cannot open a new form when begin starts cancellation of the previous form\'s Ready handle', () => {
    beginLocal();
    knownFailure();
    expect(save.begin(empty)).toBe(false);
    const cancellation = requests.expectOne((req) => req.method === 'DELETE');
    expect(save.draft()).toEqual({ intent: 'keep' });
    cancellation.flush({}, { status: 409, statusText: 'Attached' });
    expect(save.phase()).toBe('unknown');
    expect(save.begin(empty)).toBe(false);
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'DeletePending', gameId: 'original', gameExists: true });
    requests.expectOne('/original/original').flush(empty);
    expect(saved).toHaveBeenCalledOnce();
    expect(save.begin(empty)).toBe(true);
  });

  it('does not cancel a claimed receipt while the original-game refetch is pending', () => {
    unknownFailure();
    save.reconcile();
    statusRequest().flush({ ...ready(), state: 'DeletePending', gameId: 'original', gameExists: true });
    const refetch = requests.expectOne('/original/original');
    save.close();
    expect(refetch.cancelled).toBe(true);
    expect(save.phase()).toBe('unknown');
    expect(save.begin(null)).toBe(false);
    requests.expectNone((req) => req.method === 'DELETE' || req.method === 'POST');
  });

  it.each(['keep', 'remove', 'external'] as const)('keeps an unknown %s/no-handle save blocked after Close and offers only Library inspection', (intent) => {
    save.begin(empty);
    save.setDraft(intent === 'external' ? { intent, url: 'https://example.test/a.jpg' } : { intent });
    save.save(job('game'));
    requests.expectOne('/write').flush({}, { status: 503, statusText: 'Unknown outcome' });
    save.close();
    expect(save.draft()).toEqual({ intent: 'keep' });
    save.setDraft({ intent: 'local', blob: new Blob(['new image']) });
    save.save(job());
    save.reupload();
    save.retrySameHandle();
    save.reconcile();
    expect(save.begin(null)).toBe(false);
    expect(save.message()).toContain('Inspect your Library');
    requests.expectNone((req) => req.method === 'POST' || req.method === 'DELETE' || req.method === 'GET');
  });
});
