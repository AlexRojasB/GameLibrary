import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';

import { CoverImageSearchService } from './cover-image-search.service';

describe('CoverImageSearchService', () => {
  let service: CoverImageSearchService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CoverImageSearchService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('posts a structured cover-image search request without provider credentials', () => {
    let result: unknown;
    service.search({ gameType: 'VideoGame', name: 'Resident Evil 4', platformName: 'PlayStation 5' }).subscribe((value) => {
      result = value;
    });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/cover-images/search`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ gameType: 'VideoGame', name: 'Resident Evil 4', platformName: 'PlayStation 5' });
    expect(req.request.headers.has('X-Subscription-Token')).toBe(false);
    req.flush({ candidates: [] });

    expect(result).toEqual({ candidates: [] });
  });
});
