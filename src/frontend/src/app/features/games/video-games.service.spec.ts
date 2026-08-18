import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { VideoGame, VideoGameInput } from './video-game';
import { VideoGamesService } from './video-games.service';

const input: VideoGameInput = {
  name: 'Hades',
  coverImageUrl: null,
  acquisitionStatus: 'Owned',
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  rating: 5,
  notes: 'Great',
};

const game: VideoGame = { id: '1', ...input, createdAt: '2026-08-17T00:00:00Z' };

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
});