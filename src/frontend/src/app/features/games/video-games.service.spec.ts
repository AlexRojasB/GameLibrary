import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { VideoGame, VideoGameInput } from './video-game';
import { VideoGamesService } from './video-games.service';

const input: VideoGameInput = {
  name: 'Hades',
  isFamilyFriendly: true,
  acquisitionStatus: 'Owned',
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  rating: 5,
  notes: 'Great',
  minimumPlayers: 1,
  maximumPlayers: 2,
};

const game: VideoGame = { id: '1', ...input, coverImageUrl: null, coverUploadId: null, createdAt: '2026-08-17T00:00:00Z' };

describe('VideoGamesService', () => {
  let service: VideoGamesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(VideoGamesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists video games via GET /video-games', () => {
    let result: VideoGame[] | undefined;
    service.list().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/video-games`);
    expect(req.request.method).toBe('GET');
    req.flush([game]);

    expect(result).toEqual([game]);
  });

  it('reuses Library server search with a trimmed literal query, VideoGame filter and existing name sort', () => {
    let result: VideoGame[] | undefined;
    service.list('  aD%_\\  ').subscribe(value => result = value);
    const req = httpMock.expectOne(request => request.url === `${environment.apiBaseUrl}/library`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().sort()).toEqual(['gameType', 'search', 'sort']);
    expect(req.request.params.get('search')).toBe('aD%_\\');
    expect(req.request.params.get('gameType')).toBe('VideoGame');
    expect(req.request.params.get('sort')).toBe('NameAsc');
    req.flush([game]);
    expect(result).toEqual([game]);
  });

  it.each(['', '   \t '])('keeps the original full-list endpoint for empty search %j', search => {
    service.list(search).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/video-games`);
    expect(req.request.params.keys()).toEqual([]);
    req.flush([]);
  });

  it('creates a video game via POST /video-games', () => {
    let result: VideoGame | undefined;
    service.create(input).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/video-games`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(input);
    req.flush(game);

    expect(result).toEqual(game);
  });

  it('updates a video game via PUT /video-games/{id}', () => {
    let result: VideoGame | undefined;
    service.update('1', input).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/video-games/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(input);
    req.flush(game);

    expect(result).toEqual(game);
  });

  it('deletes a video game via DELETE /video-games/{id}', () => {
    let completed = false;
    service.delete('1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/video-games/1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('writes only explicit cover intent, never legacy read-model references', () => {
    const expectedCover = { coverImageUrl: null, coverUploadId: 'managed' };
    service.update('1', { ...input, ...expectedCover, coverUpdate: { intent: 'remove', expectedCover } }).subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/video-games/1`);
    expect(request.request.body).toEqual({ ...input, coverUpdate: { intent: 'remove', expectedCover } });
    request.flush(game);
  });

  it('omits the expected snapshot on Create without changing explicit intent', () => {
    service.create({ ...input, coverUpdate: { intent: 'external', url: 'https://example.test/a.jpg', expectedCover: game } }).subscribe();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/video-games`);
    expect(request.request.body.coverUpdate).toEqual({ intent: 'external', url: 'https://example.test/a.jpg' });
    request.flush(game);
  });
});
