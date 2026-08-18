import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';

import { Platform } from './platform';
import { PlatformsService } from './platforms.service';

describe('PlatformsService', () => {
  let service: PlatformsService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PlatformsService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('lists platforms via GET /platforms', () => {
    const platforms = [
      { id: '1', name: 'Steam' },
      { id: '2', name: 'Xbox' },
    ];
    let result: Platform[] | undefined;
    service.list().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/platforms`);
    expect(req.request.method).toBe('GET');
    req.flush(platforms);

    expect(result).toEqual(platforms);
  });

  it('creates a platform via POST /platforms', () => {
    let result: Platform | undefined;
    service.create('Steam').subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/platforms`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Steam' });
    req.flush({ id: '1', name: 'Steam' });

    expect(result).toEqual({ id: '1', name: 'Steam' });
  });

  it('updates a platform via PUT /platforms/{id}', () => {
    let result: Platform | undefined;
    service.update('1', 'Xbox').subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/platforms/1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ name: 'Xbox' });
    req.flush({ id: '1', name: 'Xbox' });

    expect(result).toEqual({ id: '1', name: 'Xbox' });
  });

  it('deletes a platform via DELETE /platforms/{id}', () => {
    let completed = false;
    service.delete('1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/platforms/1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });
});