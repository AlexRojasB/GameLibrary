import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';

import { SteamService } from './steam.service';

describe('SteamService', () => {
  let service: SteamService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SteamService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('calls only Game Library API routes', () => {
    service.getStatus().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`);
    expect(req.request.method).toBe('GET');
    expect(req.request.withCredentials).toBe(false);
    req.flush({ enabled: true, linkAvailable: true, importAvailable: true, linked: false, steamId64: null, linkedAt: null, message: null });
  });

  it('createLinkRequest posts to steam/link-requests', () => {
    service.createLinkRequest().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/link-requests`);
    expect(req.request.method).toBe('POST');
    expect(req.request.withCredentials).toBe(true);
    req.flush({ startUrl: 'https://api.example.test/steam/link/start/abc', expiresAt: '2026-01-01T00:00:00Z' });
  });

  it('unlink sends DELETE to steam/link', () => {
    service.unlink().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/link`);
    expect(req.request.method).toBe('DELETE');
    expect(req.request.withCredentials).toBe(false);
    req.flush(null);
  });

  it('preview posts to steam/library/preview', () => {
    service.preview().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({ state: 'Success', candidates: [], message: null });
  });

  it('import posts steamAppIds to steam/library/import', () => {
    service.import([570, 730]).subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ steamAppIds: [570, 730] });
    req.flush({ imported: [], alreadyImported: [], unavailable: [], invalid: [] });
  });

  it('no Steam Web API URL or key appears in requests', () => {
    service.getStatus().subscribe();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`);
    const url = req.request.url;
    expect(url).not.toContain('steampowered.com');
    expect(url).not.toContain('WebApiKey');
    expect(url).not.toContain('key=');
    req.flush({ enabled: true, linkAvailable: true, importAvailable: true, linked: false, steamId64: null, linkedAt: null, message: null });
  });
});
