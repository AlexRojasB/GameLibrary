import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Session } from '@supabase/supabase-js';

import { AuthService } from './auth.service';
import { SUPABASE_CLIENT } from './supabase-client';
import { API_BASE_URL, apiTokenInterceptor, isApiOrigin } from './token.interceptor';
import { createMockSupabase, createTestSession } from './testing/supabase-client.mock';

describe('apiTokenInterceptor', () => {
  const API = 'http://localhost:5218';

  function configure(apiBaseUrl: string, session: Session | null = createTestSession()): AuthService {
    const mock = createMockSupabase(session);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiTokenInterceptor])),
        provideHttpClientTesting(),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
        { provide: API_BASE_URL, useValue: apiBaseUrl },
      ],
    });
    return TestBed.inject(AuthService);
  }

  it('attaches a Bearer token to API base URL requests when a session exists', async () => {
    const auth = configure(API);
    await auth.initialize();

    const http = TestBed.inject(HttpClient);
    const httpMock = TestBed.inject(HttpTestingController);
    http.get(`${API}/auth/me`).subscribe();

    const req = httpMock.expectOne(`${API}/auth/me`);
    expect(req.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    req.flush({ userId: 'user-1' });
  });

  it('does not attach the token to a different origin', async () => {
    const auth = configure(API);
    await auth.initialize();

    const http = TestBed.inject(HttpClient);
    const httpMock = TestBed.inject(HttpTestingController);
    http.get('https://example.com/health').subscribe();

    const req = httpMock.expectOne('https://example.com/health');
    expect(req.request.headers.get('Authorization')).toBeNull();
    req.flush({});
  });

  it('does not attach the token when signed out', async () => {
    const auth = configure(API, null);
    await auth.initialize();
    expect(auth.isAuthenticated()).toBe(false);

    const http = TestBed.inject(HttpClient);
    const httpMock = TestBed.inject(HttpTestingController);
    http.get(`${API}/auth/me`).subscribe();

    const req = httpMock.expectOne(`${API}/auth/me`);
    expect(req.request.headers.get('Authorization')).toBeNull();
    req.flush({});
  });

  it('does not attach the token when apiBaseUrl is empty', async () => {
    const auth = configure('', createTestSession());
    await auth.initialize();

    const http = TestBed.inject(HttpClient);
    const httpMock = TestBed.inject(HttpTestingController);
    http.get('http://localhost:5218/auth/me').subscribe();

    const req = httpMock.expectOne('http://localhost:5218/auth/me');
    expect(req.request.headers.get('Authorization')).toBeNull();
    req.flush({});
  });
});

describe('isApiOrigin', () => {
  it('matches the configured API origin', () => {
    expect(isApiOrigin('http://localhost:5218/auth/me', 'http://localhost:5218')).toBe(true);
    expect(isApiOrigin('/health', 'http://localhost:5218')).toBe(true);
  });

  it('rejects other origins', () => {
    expect(isApiOrigin('https://example.com/auth/me', 'http://localhost:5218')).toBe(false);
    expect(isApiOrigin('http://localhost:4200/auth/me', 'http://localhost:5218')).toBe(false);
  });

  it('rejects an empty apiBaseUrl', () => {
    expect(isApiOrigin('http://localhost:5218/auth/me', '')).toBe(false);
  });
});