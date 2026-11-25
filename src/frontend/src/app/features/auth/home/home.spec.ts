import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Home } from './home';
import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { CurrentlyPlaying, CurrentlyPlayingItem } from '../../currently-playing/currently-playing.service';

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let httpMock: HttpTestingController;
  let authMock: ReturnType<typeof createMockSupabase>;

  function configure(session = createTestSession()): AuthService {
    authMock = createMockSupabase(session);
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SUPABASE_CLIENT, useValue: authMock.client },
      ],
    });
    fixture = TestBed.createComponent(Home);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return TestBed.inject(AuthService);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('calls /auth/me and displays the signed-in identity without exposing the raw user id', async () => {
    configure();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush({ userId: 'user-1' });
    httpMock.expectOne((request) => request.url.endsWith('/currently-playing')).flush({ videoGame: null, boardGame: null });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('user@example.com');
    expect(text).toContain('Play Log');
    expect(text).not.toContain('user-1');
  });

  it('renders a session-expired state and clears auth on 401', async () => {
    const auth = configure();
    await auth.initialize();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Session expired');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('renders an error state on other failures', async () => {
    configure();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush('Service Unavailable', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Unable to load your profile');
  });

  function flushProfile(): void {
    httpMock.expectOne((r) => r.url.endsWith('/auth/me')).flush({ userId: 'user-1' });
  }

  const video: CurrentlyPlayingItem = { gameId: 'video-1', libraryEntryId: 'entry-1', gameType: 'VideoGame', name: 'Hades', coverImageUrl: null, coverUploadId: null };
  const board: CurrentlyPlayingItem = { gameId: 'board-1', libraryEntryId: 'entry-2', gameType: 'BoardGame', name: 'Cascadia', coverImageUrl: null, coverUploadId: null };

  it('shows an independent sensible empty state for both types', async () => {
    configure();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: null, boardGame: null });
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Currently Playing');
    expect(text).toContain('No video game selected.');
    expect(text).toContain('No board game selected.');
    expect(text).not.toContain('Log Play Time');
  });

  it('renders both selections and refetches persisted state when Home is recreated', async () => {
    configure();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: video, boardGame: board });
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cascadia');

    fixture.destroy();
    fixture = TestBed.createComponent(Home);
    fixture.detectChanges();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({
      videoGame: { ...video, gameId: 'video-2', name: 'Zelda' }, boardGame: board,
    });
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Zelda');
    expect(text).toContain('Cascadia');
    expect(text).not.toContain('Hades');
  });

  it('does not mistake a failed slots read for an empty state and supports retry', async () => {
    configure();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush(null, { status: 503, statusText: 'Unavailable' });
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unable to load Currently Playing');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('No video game selected');
    ((fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement).click();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: video, boardGame: null });
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Hades');
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No board game selected');
  });

  it('clears the displayed selections when authentication ends', async () => {
    const auth = configure();
    await auth.initialize();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: video, boardGame: board });
    await fixture.whenStable();
    auth.clearLocalSession();
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).not.toContain('Hades');
    expect(text).not.toContain('Cascadia');
    expect(text).toContain('Session expired');
  });

  it('handles 401 while fetching the selections without displaying private data', async () => {
    const auth = configure();
    await auth.initialize();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush(null, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    expect(auth.isAuthenticated()).toBe(false);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Session expired');
  });

  const slots: CurrentlyPlaying = { videoGame: video, boardGame: board };
  const playedLocal = '2026-08-24T18:30';

  async function readyHome() {
    const auth = configure();
    await auth.initialize();
    flushProfile();
    flushSlots();
    return auth;
  }
  function flushSlots(value = slots) {
    httpMock.expectOne(r => r.method === 'GET' && r.url.endsWith('/currently-playing')).flush(value);
    fixture.detectChanges();
  }
  function host() { return fixture.nativeElement as HTMLElement; }
  function dialog() { return host().querySelector('app-log-play-dialog') as HTMLElement; }
  function button(label: string, parent = host()) {
    const result = [...parent.querySelectorAll('button')].find(b => b.textContent?.trim() === label);
    if (!result) throw new Error(`Missing button: ${label}`);
    return result;
  }
  function open(item = video) {
    const slot = host().querySelector(`[aria-label="Currently Playing ${item.gameType === 'VideoGame' ? 'Video game' : 'Board game'}"]`) as HTMLElement;
    button('Log Play Time', slot).click();
    fixture.detectChanges();
  }
  function input(selector: string, value: string) {
    const field = dialog().querySelector(selector) as HTMLInputElement;
    field.value = value;
    field.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }
  function submit(duration = '90') {
    input('input[type="datetime-local"]', playedLocal);
    input('input[type="number"]', duration);
    button('Log play', dialog()).click();
    fixture.detectChanges();
    return httpMock.expectOne(r => r.method === 'POST' && r.url.endsWith('/play-log'));
  }
  function expectNoSlotOrPickerWrites() {
    httpMock.expectNone(r => r.url.includes('/random-picker') || (r.url.endsWith('/currently-playing') && r.method !== 'GET'));
  }

  it.each([video, board])('opens the existing flow for the selected $gameType with local date and optional duration defaults', async item => {
    await readyHome();
    expect(host().querySelectorAll('.home__slot button')).toHaveLength(2);
    open(item);
    expect(dialog().textContent).toContain(item.name);
    expect((dialog().querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/);
    expect((dialog().querySelector('input[type="number"]') as HTMLInputElement).value).toBe('');
    expect(dialog().querySelector('select')).toBeNull();
    httpMock.expectNone(r => r.url.endsWith('/play-log'));
    button('Cancel', dialog()).click();
    fixture.detectChanges();
    expect(dialog().querySelector('form')).toBeNull();
    expect(fixture.componentInstance['currentlyPlaying']()).toEqual(slots);
    httpMock.expectNone(r => r.url.endsWith('/currently-playing') || r.url.endsWith('/play-log'));
    expectNoSlotOrPickerWrites();
  });

  it.each([video, board])('logs $gameType only on confirmation, prevents pending duplicates and refreshes Home on success', async item => {
    await readyHome();
    open(item);
    const request = submit();
    expect(request.request.body).toEqual({ gameId: item.gameId, playedAt: new Date(playedLocal).toISOString(), durationMinutes: 90 });
    button('Logging...', dialog()).click();
    fixture.componentInstance['onLogPlayConfirmed'](request.request.body);
    httpMock.expectNone(r => r.method === 'POST');
    expect(button('Cancel', dialog()).disabled).toBe(true);
    request.flush({ id: 'play-1' });
    expect(fixture.componentInstance['logSuccess']()).toBe('Play logged.');
    flushSlots();
    expect(dialog().querySelector('form')).toBeNull();
    expect(fixture.componentInstance['currentlyPlaying']()).toEqual(slots);
    expectNoSlotOrPickerWrites();
    // Intentional later plays remain valid, without any time-window deduplication.
    open(item);
    const next = submit('');
    expect(next.request.body.durationMinutes).toBeNull();
    next.flush({ id: 'play-2' });
    flushSlots();
  });

  it.each([400, 404, 503])('preserves slots and entered fields on logging failure %s, with no refresh or mutation', async status => {
    await readyHome();
    const before = fixture.componentInstance['currentlyPlaying']();
    open();
    submit().flush(status === 400 ? { detail: 'Only owned games can be logged.' } : null, { status, statusText: 'Failed' });
    fixture.detectChanges();
    expect(dialog().textContent).toContain(status === 400 ? 'Only owned games can be logged.' : 'Unable to log this play.');
    expect((dialog().querySelector('input[type="number"]') as HTMLInputElement).value).toBe('90');
    expect((dialog().querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toBe(playedLocal);
    expect(fixture.componentInstance['currentlyPlaying']()).toBe(before);
    expect(fixture.componentInstance['logSuccess']()).toBe('');
    httpMock.expectNone(r => r.url.endsWith('/currently-playing'));
    expectNoSlotOrPickerWrites();
    submit().flush({ id: 'retry' });
    flushSlots();
  });

  it.each(['date', 'duration'])('retains existing required-date/positive-duration validation (%s)', async invalid => {
    await readyHome();
    open();
    if (invalid === 'date') input('input[type="datetime-local"]', '');
    else input('input[type="number"]', '0');
    button('Log play', dialog()).click();
    fixture.detectChanges();
    expect(dialog().textContent).toContain(invalid === 'date' ? 'Choose when you played.' : 'Duration must be a whole number greater than 0.');
    httpMock.expectNone(r => r.url.endsWith('/play-log'));
    expectNoSlotOrPickerWrites();
  });

  it('keeps a successful-save confirmation distinct from a failed Home refresh', async () => {
    await readyHome();
    const before = fixture.componentInstance['currentlyPlaying']();
    open();
    submit().flush({ id: 'saved' });
    httpMock.expectOne(r => r.url.endsWith('/currently-playing')).flush(null, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(host().textContent).toContain('Play logged.');
    expect(host().textContent).toContain('Unable to load Currently Playing.');
    expect(fixture.componentInstance['currentlyPlaying']()).toBe(before);
    button('Retry').click();
    flushSlots();
    expectNoSlotOrPickerWrites();
  });

  it('does not silently retarget an open dialog if the selected slot is replaced', async () => {
    await readyHome();
    open();
    fixture.componentInstance['loadCurrentlyPlaying']();
    flushSlots({ ...slots, videoGame: { ...video, gameId: 'video-new', name: 'Replacement' } });
    const request = submit();
    expect(request.request.body.gameId).toBe(video.gameId);
    request.flush({ id: 'saved' });
    flushSlots({ ...slots, videoGame: { ...video, gameId: 'video-new', name: 'Replacement' } });
    expect(host().textContent).toContain('Replacement');
    expectNoSlotOrPickerWrites();
  });

  it('uses normal Home session-expiry handling on a logging 401', async () => {
    const auth = await readyHome();
    open();
    submit().flush(null, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(auth.isAuthenticated()).toBe(false);
    expect(dialog().querySelector('form')).toBeNull();
    expect(host().textContent).toContain('Session expired');
    expect(host().textContent).not.toContain(video.name);
    expectNoSlotOrPickerWrites();
  });

  it.each(['logout', 'account switch', 'navigation'])('cancels pending logging and never applies late feedback after %s', async change => {
    const auth = await readyHome();
    open();
    const pending = submit();
    if (change === 'navigation') fixture.destroy();
    else if (change === 'logout') auth.clearLocalSession();
    else authMock.emit('SIGNED_IN', createTestSession({ user: { ...createTestSession().user, id: 'other' } }));
    expect(pending.cancelled).toBe(true);
    if (change !== 'navigation') {
      fixture.detectChanges();
      expect(dialog().querySelector('form')).toBeNull();
      expect(fixture.componentInstance['logPendingGameId']()).toBeNull();
      expect(fixture.componentInstance['logSuccess']()).toBe('');
      if (change === 'account switch') {
        flushProfile();
        flushSlots({ videoGame: null, boardGame: null });
      }
      expect(host().textContent).not.toContain(video.name);
    }
    expectNoSlotOrPickerWrites();
  });
});
