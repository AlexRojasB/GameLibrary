import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { authGuard } from '../../../core/auth/auth.guard';
import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { routes } from '../../../app.routes';
import { PlayLogEntry } from '../play-log';

import { PlayLogPage } from './play-log-page';

const olderEntry: PlayLogEntry = {
  id: 'log-1',
  libraryEntryId: 'entry-1',
  gameId: 'game-1',
  gameType: 'VideoGame',
  gameName: 'Hades',
  coverImageUrl: null,
  playedAt: '2026-08-24T18:30:00Z',
  durationMinutes: null,
  createdAt: '2026-08-24T18:30:00Z',
};

const newerEntry: PlayLogEntry = {
  id: 'log-2',
  libraryEntryId: 'entry-2',
  gameId: 'game-2',
  gameType: 'BoardGame',
  gameName: 'Cascadia',
  coverImageUrl: 'https://example.com/cascadia.jpg',
  playedAt: '2026-08-25T18:30:00Z',
  durationMinutes: 90,
  createdAt: '2026-08-25T18:30:00Z',
};

@Component({ selector: 'app-play-log-login-stub', template: '' })
class LoginStub {}

function configure(): { fixture: ComponentFixture<PlayLogPage>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [PlayLogPage],
    providers: [
      provideRouter([{ path: 'login', component: LoginStub }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ],
  });
  const fixture = TestBed.createComponent(PlayLogPage);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function playLogRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/play-log'));
}

function updateRequests(httpMock: HttpTestingController) {
  return httpMock.match((request) => request.method === 'PUT' && request.url.includes('/play-log/'));
}

function text(fixture: ComponentFixture<PlayLogPage>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function buttonByText(fixture: ComponentFixture<PlayLogPage>, label: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === label,
  );
  expect(button).toBeTruthy();
  return button as HTMLButtonElement;
}

function cardByGame(fixture: ComponentFixture<PlayLogPage>, gameName: string): HTMLElement {
  const card = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.play-log-card')].find((candidate) =>
    candidate.textContent?.includes(gameName),
  );
  expect(card).toBeTruthy();
  return card as HTMLElement;
}

function fillDialog(fixture: ComponentFixture<PlayLogPage>, playedAt: string, durationMinutes: string): void {
  const host = (fixture.nativeElement as HTMLElement).querySelector('app-log-play-dialog') as HTMLElement;
  const playedAtInput = host.querySelector('input[type="datetime-local"]') as HTMLInputElement;
  const durationInput = host.querySelector('input[type="number"]') as HTMLInputElement;

  playedAtInput.value = playedAt;
  playedAtInput.dispatchEvent(new Event('input'));
  durationInput.value = durationMinutes;
  durationInput.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('PlayLogPage', () => {
  let fixture: ComponentFixture<PlayLogPage>;
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
    expect(routes.find((route) => route.path === 'play-log')?.canActivate).toEqual([authGuard]);

    playLogRequest(httpMock).flush([]);
  });

  it('shows loading while the request is pending', () => {
    expect(text(fixture)).toContain('Loading');

    playLogRequest(httpMock).flush([]);
  });

  it('renders an empty state', async () => {
    playLogRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('No plays logged yet');
    expect(text(fixture)).toContain('Browse Library');
  });

  it('renders returned entries in API order with browser-local formatted timestamps', async () => {
    playLogRequest(httpMock).flush([newerEntry, olderEntry]);
    await fixture.whenStable();
    fixture.detectChanges();

    const body = text(fixture);
    expect(body.indexOf('Cascadia')).toBeLessThan(body.indexOf('Hades'));
    expect(body).toContain('Board game');
    expect(body).toContain('Video game');
    expect(body).toContain('Duration: 1 h 30 min');
    expect(body).toContain('No cover');
    expect(body).toContain('2026');
    expect(body).not.toContain('2026-08-24T18:30:00Z');
    expect(cardByGame(fixture, 'Hades').textContent).not.toContain('Duration:');
  });

  it('shows error and retries', async () => {
    playLogRequest(httpMock).flush('Server Error', { status: 500, statusText: 'Server Error' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Unable to load your Play Log');
    ((fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    playLogRequest(httpMock).flush([olderEntry]);
  });

  it('clears the session and navigates to login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);

    playLogRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });

  it('opens edit prepopulated with read-only game identity and cancels without PUT', async () => {
    playLogRequest(httpMock).flush([newerEntry, olderEntry]);
    await fixture.whenStable();
    fixture.detectChanges();

    const card = cardByGame(fixture, 'Cascadia');
    (card.querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();

    const host = (fixture.nativeElement as HTMLElement).querySelector('app-log-play-dialog') as HTMLElement;
    expect(host.textContent).toContain('Edit play log');
    expect(host.textContent).toContain('Cascadia');
    expect(host.textContent).toContain('The game cannot be changed');
    expect((host.querySelector('input[type="number"]') as HTMLInputElement).value).toBe('90');
    expect(host.querySelector('select')).toBeNull();

    buttonByText(fixture, 'Cancel').click();
    fixture.detectChanges();

    expect(updateRequests(httpMock)).toHaveLength(0);
  });

  it('saves edits with only editable fields, updates the card, and reorders by PlayedAt', async () => {
    playLogRequest(httpMock).flush([newerEntry, olderEntry]);
    await fixture.whenStable();
    fixture.detectChanges();

    (cardByGame(fixture, 'Hades').querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    fillDialog(fixture, '2026-08-26T12:00', '120');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const requests = updateRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].request.url.endsWith('/play-log/log-1')).toBe(true);
    expect(requests[0].request.body).toEqual({
      playedAt: new Date('2026-08-26T12:00').toISOString(),
      durationMinutes: 120,
    });
    expect(Object.keys(requests[0].request.body)).toEqual(['playedAt', 'durationMinutes']);

    requests[0].flush({ ...olderEntry, playedAt: '2026-08-26T18:00:00Z', durationMinutes: 120 });
    await fixture.whenStable();
    fixture.detectChanges();

    const body = text(fixture);
    expect(body.indexOf('Hades')).toBeLessThan(body.indexOf('Cascadia'));
    expect(cardByGame(fixture, 'Hades').textContent).toContain('Duration: 2 h');
    expect(text(fixture)).not.toContain('Edit play log');
  });

  it('prevents duplicate edit saves, preserves form state on error, and allows clearing duration', async () => {
    playLogRequest(httpMock).flush([newerEntry]);
    await fixture.whenStable();
    fixture.detectChanges();

    (cardByGame(fixture, 'Cascadia').querySelector('button') as HTMLButtonElement).click();
    fixture.detectChanges();
    fillDialog(fixture, '2026-08-25T12:00', '');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();
    buttonByText(fixture, 'Saving...').click();
    fixture.detectChanges();

    let requests = updateRequests(httpMock);
    expect(requests).toHaveLength(1);
    expect(requests[0].request.body.durationMinutes).toBeNull();
    requests[0].flush({ detail: 'Duration minutes must be greater than 0.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    const host = (fixture.nativeElement as HTMLElement).querySelector('app-log-play-dialog') as HTMLElement;
    expect(host.textContent).toContain('Duration minutes must be greater than 0.');
    expect((host.querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toBe('2026-08-25T12:00');
    expect((host.querySelector('input[type="number"]') as HTMLInputElement).value).toBe('');

    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();
    requests = updateRequests(httpMock);
    expect(requests).toHaveLength(1);
    requests[0].flush({ ...newerEntry, durationMinutes: null });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(cardByGame(fixture, 'Cascadia').textContent).not.toContain('Duration:');
  });
});
