import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Home } from './home';
import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let httpMock: HttpTestingController;

  function configure(session = createTestSession()): AuthService {
    const mock = createMockSupabase(session);
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
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

  const video = { gameId: 'video-1', libraryEntryId: 'entry-1', gameType: 'VideoGame', name: 'Hades', coverImageUrl: null, coverUploadId: null };
  const board = { gameId: 'board-1', libraryEntryId: 'entry-2', gameType: 'BoardGame', name: 'Cascadia', coverImageUrl: null, coverUploadId: null };

  it('shows an independent sensible empty state for both types', async () => {
    configure();
    flushProfile();
    httpMock.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: null, boardGame: null });
    await fixture.whenStable();
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Currently Playing');
    expect(text).toContain('No video game selected.');
    expect(text).toContain('No board game selected.');
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
});
