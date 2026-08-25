import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { PlayLogEntry } from './play-log';
import { PlayLogService } from './play-log.service';

const entry: PlayLogEntry = {
  id: 'log-1',
  libraryEntryId: 'entry-1',
  gameId: 'game-1',
  gameType: 'VideoGame',
  gameName: 'Hades',
  coverImageUrl: null,
  playedAt: '2026-08-24T18:30:00Z',
  durationMinutes: 90,
  createdAt: '2026-08-24T18:30:00Z',
};

describe('PlayLogService', () => {
  let service: PlayLogService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PlayLogService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('calls GET /play-log', () => {
    let result: PlayLogEntry[] | undefined;
    service.list().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/play-log`);
    expect(req.request.method).toBe('GET');
    req.flush([entry]);

    expect(result).toEqual([entry]);
  });

  it('posts reviewed play details to /play-log', () => {
    let result: PlayLogEntry | undefined;
    service.logPlay({ gameId: 'game-1', playedAt: '2026-08-24T18:30:00.000Z', durationMinutes: 90 }).subscribe((value) =>
      (result = value),
    );

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/play-log`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ gameId: 'game-1', playedAt: '2026-08-24T18:30:00.000Z', durationMinutes: 90 });
    expect(Object.keys(req.request.body)).toEqual(['gameId', 'playedAt', 'durationMinutes']);
    req.flush(entry);

    expect(result).toEqual(entry);
  });

  it('puts editable play details to /play-log/{id}', () => {
    let result: PlayLogEntry | undefined;
    service.update('log-1', { playedAt: '2026-08-24T18:30:00.000Z', durationMinutes: null }).subscribe((value) =>
      (result = value),
    );

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/play-log/log-1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ playedAt: '2026-08-24T18:30:00.000Z', durationMinutes: null });
    expect(Object.keys(req.request.body)).toEqual(['playedAt', 'durationMinutes']);
    req.flush({ ...entry, durationMinutes: null });

    expect(result).toEqual({ ...entry, durationMinutes: null });
  });
});
