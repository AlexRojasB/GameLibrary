import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { API_BASE_URL, apiTokenInterceptor } from '../../../core/auth/token.interceptor';
import { LibraryItem } from '../../library/library';
import { LibraryPage } from '../../library/library-page/library-page';
import { PlayLogEntry } from '../../play-log/play-log';
import { PlayLogPage } from '../../play-log/play-log-page/play-log-page';
import { PickerRound, RandomPickerItem } from '../../random-picker/random-picker';
import { RandomPickerPage } from '../../random-picker/random-picker-page/random-picker-page';

import { mockCoverVisibility } from './cover-image.testing';

const ID = '00000000-0000-0000-0000-000000000001';
const video: LibraryItem = {
  id: 'game-1', gameType: 'VideoGame', name: 'Hades', coverImageUrl: null, coverUploadId: ID,
  isFamilyFriendly: false, createdAt: '2026-08-17T00:00:00Z', acquisitionStatus: 'Owned', rating: 5,
  notes: 'Fast runs', platformIds: [], genreIds: [], gameStatus: 'Playing', progressPercentage: 50,
  minimumPlayers: 1, maximumPlayers: 1, approximateDuration: null, interactionType: null,
};
const external: LibraryItem = {
  ...video, id: 'game-2', gameType: 'BoardGame', name: 'Pandemic', coverImageUrl: 'https://external.test/pandemic.jpg',
  coverUploadId: null, gameStatus: null, progressPercentage: null,
};

describe('Managed cover page integration', () => {
  let fixture: ComponentFixture<LibraryPage | RandomPickerPage | PlayLogPage>;
  let http: HttpTestingController;
  let observers: ReturnType<typeof mockCoverVisibility>;
  let createUrl: ReturnType<typeof vi.fn<(blob: Blob | MediaSource) => string>>;
  let revokeUrl: ReturnType<typeof vi.fn<(url: string) => void>>;

  beforeEach(async () => {
    observers = mockCoverVisibility();
    let serial = 0;
    createUrl = vi.fn(() => `blob:display-${++serial}`);
    revokeUrl = vi.fn();
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = createUrl;
      static override revokeObjectURL = revokeUrl;
    });
    const mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({ providers: [
      provideRouter([]), provideHttpClient(withInterceptors([apiTokenInterceptor])), provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: 'https://api.example.test' },
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ] });
    await TestBed.inject(AuthService).initialize();
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    try {
      fixture?.destroy();
      http.verify();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  function flushLookups(): void {
    http.expectOne((request) => request.url.endsWith('/platforms')).flush([]);
    http.expectOne((request) => request.url.endsWith('/genres')).flush([]);
  }

  function contentRequest() {
    const request = http.expectOne((request) => request.url.endsWith(`/cover-uploads/${ID}/content`));
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    return request;
  }

  function sources(selector: string): (string | null)[] {
    return [...(fixture.nativeElement as HTMLElement).querySelectorAll(selector)].map((image) => image.getAttribute('src'));
  }

  it('preserves Library image classes, layout hooks and model references for managed and direct external covers', () => {
    const page = TestBed.createComponent(LibraryPage);
    fixture = page;
    page.detectChanges();
    flushLookups();
    http.expectOne((request) => request.url.endsWith('/library')).flush([video, external]);
    page.detectChanges();
    http.expectNone((request) => request.url.endsWith('/content'));
    observers.forEach((observer) => observer.show());
    page.detectChanges();
    contentRequest().flush(new Blob());
    page.detectChanges();
    expect(sources('img.library-card__cover')).toEqual(['blob:display-1', external.coverImageUrl]);
    expect(page.componentInstance['items']()).toEqual([video, external]);
    expect(createUrl).toHaveBeenCalledOnce();
    http.expectNone((request) => request.url.startsWith('https://external.test'));
    page.destroy();
    expect(revokeUrl).toHaveBeenCalledExactlyOnceWith('blob:display-1');
  });

  it('reauthorizes Picker history covers and preserves order, shown IDs and result states after a retired-image failure', () => {
    const page = TestBed.createComponent(RandomPickerPage);
    fixture = page;
    page.detectChanges();
    flushLookups();
    const first: RandomPickerItem = { ...video, libraryEntryId: 'entry-1', gameId: video.id };
    const second: RandomPickerItem = { ...external, libraryEntryId: 'entry-2', gameId: external.id };
    const emptyRound: PickerRound = {
      mode: 'All', revision: 0,
      filters: { platformIds: [], genreIds: [], gameStatuses: [], playerCount: null,
        availableDuration: null, interactionTypes: [], ratingMin: null, familyFriendlyOnly: false },
      state: null, result: null, history: [], shownLibraryEntryIds: [], proposalUnavailable: false,
    };
    expect(page.componentInstance['ready']()).toBe(false);
    http.expectOne((request) => request.method === 'GET' && request.url.endsWith('/random-picker/rounds/All')).flush(emptyRound);
    page.detectChanges();
    expect(page.componentInstance['ready']()).toBe(true);
    page.componentInstance['pick']();
    const initialPick = http.expectOne((request) => request.method === 'POST' && request.url.endsWith('/random-picker/pick'));
    expect(initialPick.request.body.roundRevision).toBe(0);
    initialPick.flush({ state: 'SUCCESS', result: first,
      round: { ...emptyRound, revision: 1, state: 'SUCCESS', result: first,
        history: [first], shownLibraryEntryIds: ['entry-1'] } satisfies PickerRound });
    page.detectChanges();
    observers[0].show();
    page.detectChanges();
    contentRequest().flush(new Blob());
    page.detectChanges();
    expect(sources('.random-picker-card--current img')).toEqual(['blob:display-1']);

    page.componentInstance['another']();
    const anotherPick = http.expectOne((request) => request.method === 'POST' && request.url.endsWith('/random-picker/pick'));
    expect(anotherPick.request.body.roundRevision).toBe(1);
    const acknowledgedRound: PickerRound = { ...emptyRound, revision: 2, state: 'SUCCESS', result: second,
      history: [second, first], shownLibraryEntryIds: ['entry-1', 'entry-2'] };
    anotherPick.flush({ state: 'SUCCESS', result: second, round: acknowledgedRound });
    page.detectChanges();
    observers.at(-1)!.show();
    page.detectChanges();
    const state = page.componentInstance['currentState']();
    const result = page.componentInstance['currentResult']();
    const history = page.componentInstance['visibleHistory']();
    const shown = page.componentInstance['shownLibraryEntryIds']();
    expect(history).toEqual([second, first]);
    expect(shown).toEqual(['entry-1', 'entry-2']);
    contentRequest().flush(new Blob(), { status: 404, statusText: 'Retired' });
    page.detectChanges();
    expect(page.componentInstance['currentState']()).toBe(state);
    expect(page.componentInstance['currentResult']()).toBe(result);
    expect(page.componentInstance['visibleHistory']()).toBe(history);
    expect(page.componentInstance['shownLibraryEntryIds']()).toBe(shown);
    expect((page.nativeElement as HTMLElement).querySelector('.random-picker-card--history')?.textContent).toContain('Cover unavailable');
    expect(sources('.random-picker-card--current img')).toEqual([external.coverImageUrl]);

    page.componentInstance['another']();
    const next = http.expectOne((request) => request.url.endsWith('/random-picker/pick'));
    expect(next.request.body).toMatchObject({ mode: 'All', roundRevision: 2 });
    expect(next.request.body).not.toHaveProperty('shownLibraryEntryIds');
    next.flush({ state: 'ALL_ALREADY_SHOWN', result: null,
      round: { ...acknowledgedRound, revision: 3, state: 'ALL_ALREADY_SHOWN', result: null } satisfies PickerRound });
    page.detectChanges();
    expect(page.componentInstance['visibleHistory']()).toEqual([second, first]);
    expect(page.componentInstance['currentState']()).toBe('ALL_ALREADY_SHOWN');
  });

  it('deduplicates simultaneous repeated Play Log cover reads while retaining separate disposable presentation', () => {
    const page = TestBed.createComponent(PlayLogPage);
    fixture = page;
    page.detectChanges();
    const older: PlayLogEntry = {
      id: 'play-1', libraryEntryId: 'entry-1', gameId: video.id, gameType: video.gameType, gameName: video.name,
      coverImageUrl: null, coverUploadId: ID, playedAt: '2026-08-25T18:00:00Z', durationMinutes: null, createdAt: '2026-08-25T18:00:00Z',
    };
    const recent: PlayLogEntry = { ...older, id: 'play-2', playedAt: '2026-08-26T18:00:00Z', durationMinutes: 60 };
    http.expectOne((request) => request.url.endsWith('/play-log')).flush([recent, older]);
    page.detectChanges();
    observers.forEach((observer) => observer.show());
    page.detectChanges();
    contentRequest().flush(new Blob());
    page.detectChanges();
    expect(sources('img.play-log-card__cover')).toEqual(['blob:display-1', 'blob:display-2']);
    expect(page.componentInstance['entries']()).toEqual([recent, older]);
    expect(page.nativeElement.textContent).toContain('Duration: 1 h');
    page.destroy();
    expect(revokeUrl.mock.calls).toEqual([['blob:display-1'], ['blob:display-2']]);
  });
});
