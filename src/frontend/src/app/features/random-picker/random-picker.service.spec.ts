import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { RandomPickerMode, RandomPickerPickRequest, RandomPickerPickResponse } from './random-picker';
import { RandomPickerService } from './random-picker.service';

describe('RandomPickerService', () => {
  let service: RandomPickerService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(RandomPickerService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it.each([
    ['VideoGames', false], ['VideoGames', true],
    ['BoardGames', false], ['BoardGames', true],
    ['All', false], ['All', true],
  ] as const)('posts %s with explicit familyFriendlyOnly=%s', (mode: RandomPickerMode, familyFriendlyOnly: boolean) => {
    const request: RandomPickerPickRequest = {
      mode,
      shownLibraryEntryIds: ['shown-1'],
      platformIds: mode === 'VideoGames' ? ['p1'] : [],
      genreIds: mode === 'VideoGames' ? ['g1'] : [],
      gameStatuses: mode === 'VideoGames' ? ['Backlog'] : [],
      playerCount: null,
      availableDuration: null,
      interactionTypes: [],
      ratingMin: null,
      familyFriendlyOnly,
    };
    let result: RandomPickerPickResponse | undefined;

    service.pick(request).subscribe((value) => (result = value));

    const req = httpMock.expectOne((httpRequest) => httpRequest.url === `${environment.apiBaseUrl}/random-picker/pick`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    req.flush({ state: 'NO_CANDIDATES', result: null });

    expect(result).toEqual({ state: 'NO_CANDIDATES', result: null });
  });
});
