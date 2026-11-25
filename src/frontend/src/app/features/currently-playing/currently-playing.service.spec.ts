import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { CurrentlyPlayingService } from './currently-playing.service';

describe('CurrentlyPlayingService', () => {
  let service: CurrentlyPlayingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(CurrentlyPlayingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('loads both persisted slots freshly, without a client owner identity', () => {
    service.get().subscribe((slots) => expect(slots).toEqual({ videoGame: null, boardGame: null }));
    const request = http.expectOne((r) => r.url.endsWith('/currently-playing'));
    expect(request.request.method).toBe('GET');
    expect(request.request.params.keys()).toEqual([]);
    request.flush({ videoGame: null, boardGame: null });
    service.get().subscribe();
    http.expectOne((r) => r.url.endsWith('/currently-playing')).flush({ videoGame: null, boardGame: null });
  });

  it('selects by Game identity only and lets the server determine Library and type', () => {
    service.select('game-1').subscribe();
    const request = http.expectOne((r) => r.url.endsWith('/currently-playing'));
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ gameId: 'game-1' });
    request.flush({ videoGame: null, boardGame: null });
  });
});
