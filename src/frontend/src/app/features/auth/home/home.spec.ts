import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Home } from './home';
import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let httpMock: HttpTestingController;

  function configure(session = createTestSession()): AuthService {
    const mock = createMockSupabase(session);
    TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SUPABASE_CLIENT, useValue: mock.client },
      ],
    });
    fixture = TestBed.createComponent(Home);
    httpMock = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return TestBed.inject(AuthService);
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('calls /auth/me and displays the signed-in identity without exposing the raw user id', async () => {
    configure();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush({ userId: 'user-1' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('user@example.com');
    expect(text).not.toContain('user-1');
  });

  it('renders a session-expired state and clears auth on 401', async () => {
    const auth = configure();
    await auth.initialize();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Session expired');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('renders an error state on other failures', async () => {
    configure();

    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/auth/me'));
    req.flush('Service Unavailable', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Unable to load your profile');
  });
});
