import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { BoardGame, BoardGameInput } from './board-game';
import { BoardGamesService } from './board-games.service';

const input: BoardGameInput = {
  name: 'Catan',
  minimumPlayers: 3,
  maximumPlayers: 4,
  approximateDuration: 60,
  interactionType: 'Cooperative',
  acquisitionStatus: 'Owned',
  rating: 5,
  notes: 'Great',
  coverImageUrl: null,
};

const game: BoardGame = {
  id: '1',
  name: 'Catan',
  coverImageUrl: null,
  minimumPlayers: 3,
  maximumPlayers: 4,
  approximateDuration: 60,
  interactionType: 'Cooperative',
  acquisitionStatus: 'Owned',
  rating: 5,
  notes: 'Great',
  createdAt: '2026-08-17T00:00:00Z',
};

describe('BoardGamesService', () => {
  let service: BoardGamesService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BoardGamesService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists board games via GET /board-games', () => {
    let result: BoardGame[] | undefined;
    service.list().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/board-games`);
    expect(req.request.method).toBe('GET');
    req.flush([game]);

    expect(result).toEqual([game]);
  });

  it('creates a board game via POST /board-games', () => {
    let result: BoardGame | undefined;
    service.create(input).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/board-games`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(input);
    req.flush(game);

    expect(result).toEqual(game);
  });

  it('updates a board game via PUT /board-games/{id}', () => {
    let result: BoardGame | undefined;
    service.update('1', input).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/board-games/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(input);
    req.flush(game);

    expect(result).toEqual(game);
  });

  it('deletes a board game via DELETE /board-games/{id}', () => {
    let completed = false;
    service.delete('1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/board-games/1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });
});