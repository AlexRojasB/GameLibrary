import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';

import { environment } from '../../../../environments/environment';
import { AuthService } from '../../../core/auth/auth.service';
import { apiTokenInterceptor } from '../../../core/auth/token.interceptor';
import { CoverUploadsService } from './cover-uploads.service';

describe('authenticated cover upload API', () => {
  let service: CoverUploadsService;
  let http: HttpTestingController;
  const id = '11111111-1111-4111-8111-111111111111';

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([apiTokenInterceptor])), provideHttpClientTesting(),
      { provide: AuthService, useValue: { accessToken: signal('test-access-token') } },
    ] });
    service = TestBed.inject(CoverUploadsService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('uploads exactly one JPEG Blob with browser-owned multipart boundary and Bearer auth', () => {
    const blob = new Blob(['normalized browser cover'], { type: 'image/jpeg' });
    service.upload(blob).subscribe();
    const request = http.expectOne(`${environment.apiBaseUrl}/cover-uploads`);
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    expect(request.request.headers.has('Content-Type')).toBe(false);
    const body = request.request.body as FormData;
    expect([...body.keys()]).toEqual(['file']);
    expect((body.get('file') as File).name).toBe('cover.jpg');
    expect((body.get('file') as File).type).toBe('image/jpeg');
    request.flush({ id, state: 'Ready', gameId: null, gameExists: null, expiresAt: '2026-11-01T00:00:00Z' });
  });

  it('obtains public options through the authenticated application API only', () => {
    service.options().subscribe();
    const request = http.expectOne(`${environment.apiBaseUrl}/cover-uploads/options`);
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    request.flush({ enabled: false });
  });

  it('reads receipt metadata and cancels only the owned opaque handle via the API', () => {
    service.status(id).subscribe();
    http.expectOne(`${environment.apiBaseUrl}/cover-uploads/${id}`).flush({ id, state: 'Ready' });
    service.cancel(id).subscribe();
    const request = http.expectOne(`${environment.apiBaseUrl}/cover-uploads/${id}`);
    expect(request.request.method).toBe('DELETE');
    expect(request.request.body).toBeNull();
    request.flush(null, { status: 202, statusText: 'Accepted' });
  });

  it('sends only the cover intent and explicit nullable CAS snapshot for cover-only updates', () => {
    const coverUpdate = { intent: 'remove' as const, expectedCover: { coverImageUrl: null, coverUploadId: id } };
    service.update(id, coverUpdate).subscribe();
    const request = http.expectOne(`${environment.apiBaseUrl}/games/${id}/cover`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ coverUpdate });
    request.flush({ gameId: id, coverImageUrl: null, coverUploadId: null });
  });
});
