import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { LibraryFilterState, LibraryItem } from './library';
import { LibraryService } from './library.service';

const emptyFilters: LibraryFilterState = {
  search: '',
  gameType: null,
  acquisitionStatuses: [],
  platformIds: [],
  genreIds: [],
  ratingMin: null,
  playerCount: null,
  interactionTypes: [],
  gameStatuses: [],
};

const item: LibraryItem = {
  id: '1',
  gameType: 'VideoGame',
  name: 'Hades',
  coverImageUrl: null,
  createdAt: '2026-08-17T00:00:00Z',
  acquisitionStatus: 'Owned',
  rating: 5,
  notes: null,
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  minimumPlayers: null,
  maximumPlayers: null,
  approximateDuration: null,
  interactionType: null,
};

describe('LibraryService', () => {
  let service: LibraryService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(LibraryService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('omits empty filters and includes the selected sort', () => {
    let result: LibraryItem[] | undefined;
    service.browse(emptyFilters, 'NameAsc').subscribe((value) => (result = value));

    const req = httpMock.expectOne((request) => request.url === `${environment.apiBaseUrl}/library`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual(['sort']);
    expect(req.request.params.get('sort')).toBe('NameAsc');
    req.flush([item]);

    expect(result).toEqual([item]);
  });

  it('serializes repeated multi-select params without CSV', () => {
    service
      .browse(
        {
          search: '  hades  ',
          gameType: 'VideoGame',
          acquisitionStatuses: ['Owned', 'Wishlist'],
          platformIds: ['p1', 'p2'],
          genreIds: ['g1'],
          ratingMin: 4,
          playerCount: 2,
          interactionTypes: ['Cooperative'],
          gameStatuses: ['Playing', 'Completed'],
        },
        'RatingDesc',
      )
      .subscribe();

    const req = httpMock.expectOne((request) => request.url === `${environment.apiBaseUrl}/library`);
    expect(req.request.params.get('search')).toBe('hades');
    expect(req.request.params.get('gameType')).toBe('VideoGame');
    expect(req.request.params.get('ratingMin')).toBe('4');
    expect(req.request.params.get('playerCount')).toBe('2');
    expect(req.request.params.get('sort')).toBe('RatingDesc');
    expect(req.request.params.getAll('acquisitionStatuses')).toEqual(['Owned', 'Wishlist']);
    expect(req.request.params.getAll('platformIds')).toEqual(['p1', 'p2']);
    expect(req.request.params.getAll('genreIds')).toEqual(['g1']);
    expect(req.request.params.getAll('interactionTypes')).toEqual(['Cooperative']);
    expect(req.request.params.getAll('gameStatuses')).toEqual(['Playing', 'Completed']);
    expect(req.request.urlWithParams).toContain('platformIds=p1&platformIds=p2');
    expect(req.request.urlWithParams).not.toContain('platformIds=p1,p2');
    req.flush([]);
  });
});
