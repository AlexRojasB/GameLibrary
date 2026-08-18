import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { Genre } from './genres';
import { GenresService } from './genres.service';

describe('GenresService', () => {
  let service: GenresService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(GenresService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists genres via GET /genres', () => {
    const genres: Genre[] = [
      { id: 'g1', name: 'Action' },
      { id: 'g2', name: 'RPG' },
    ];
    let result: Genre[] | undefined;
    service.list().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/genres`);
    expect(req.request.method).toBe('GET');
    req.flush(genres);

    expect(result).toEqual(genres);
  });
});