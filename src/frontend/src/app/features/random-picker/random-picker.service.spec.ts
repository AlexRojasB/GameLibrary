import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { RandomPickerPickRequest, RandomPickerPickResponse } from './random-picker';
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

  it('posts the expected body to the random picker endpoint', () => {
    const request: RandomPickerPickRequest = {
      mode: 'VideoGames',
      shownLibraryEntryIds: ['shown-1'],
      platformIds: ['p1'],
      genreIds: ['g1'],
      gameStatuses: ['Backlog'],
      playerCount: null,
      availableDuration: null,
      interactionTypes: [],
      ratingMin: null,
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
