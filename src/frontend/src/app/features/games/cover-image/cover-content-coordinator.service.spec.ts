import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { API_BASE_URL, apiTokenInterceptor } from '../../../core/auth/token.interceptor';

import { CoverContentCoordinator } from './cover-content-coordinator.service';

const API = 'https://api.example.test/app';
const id = (value: number) => `00000000-0000-0000-0000-${value.toString().padStart(12, '0')}`;
const contentUrl = (value: number) => `${API}/cover-uploads/${id(value)}/content`;

describe('CoverContentCoordinator', () => {
  let coordinator: CoverContentCoordinator;
  let http: HttpTestingController;
  let auth: AuthService;
  let mock: ReturnType<typeof createMockSupabase>;

  beforeEach(async () => {
    mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({ providers: [
      provideHttpClient(withInterceptors([apiTokenInterceptor])), provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: API },
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ] });
    auth = TestBed.inject(AuthService);
    await auth.initialize();
    coordinator = TestBed.inject(CoverContentCoordinator);
    http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });

  afterEach(() => http.verify());

  it('uses the configured API, Blob/no-store transport and existing token interceptor', () => {
    coordinator.content(id(1)).subscribe();
    const request = http.expectOne(contentUrl(1));
    expect(request.request.responseType).toBe('blob');
    expect(request.request.cache).toBe('no-store');
    expect(request.request.headers.get('ngsw-bypass')).toBe('true');
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    expect(request.request.url).not.toContain('?');
    request.flush(new Blob(['jpeg'], { type: 'image/jpeg' }));

    TestBed.inject(HttpClient).get('https://external.example.test/image.jpg').subscribe();
    const external = http.expectOne('https://external.example.test/image.jpg');
    expect(external.request.headers.has('Authorization')).toBe(false);
    external.flush({});
  });

  it('deduplicates in-flight subscribers but gives a later subscriber a new request', () => {
    const first = vi.fn();
    const second = vi.fn();
    const bytes = new Blob(['jpeg']);
    coordinator.content(id(1)).subscribe(first);
    coordinator.content(id(1)).subscribe(second);
    http.expectOne(contentUrl(1)).flush(bytes);
    expect(first).toHaveBeenCalledExactlyOnceWith(bytes);
    expect(second).toHaveBeenCalledExactlyOnceWith(bytes);
    coordinator.content(id(1)).subscribe();
    http.expectOne(contentUrl(1)).flush(bytes);
  });

  it('retains no completed errors and permits an explicit new request', () => {
    const error = vi.fn();
    coordinator.content(id(1)).subscribe({ error });
    http.expectOne(contentUrl(1)).flush(new Blob(), { status: 404, statusText: 'Not found' });
    expect(error).toHaveBeenCalledOnce();
    coordinator.content(id(1)).subscribe();
    http.expectOne(contentUrl(1)).flush(new Blob());
  });

  it('caps active requests at four, deduplicates queued IDs and removes canceled queued work', () => {
    const subscriptions = Array.from({ length: 7 }, (_, index) => coordinator.content(id(index + 1)).subscribe());
    const duplicate = coordinator.content(id(5)).subscribe();
    const active = http.match(() => true);
    expect(active.map((request) => request.request.url)).toEqual([1, 2, 3, 4].map(contentUrl));
    subscriptions[5].unsubscribe();
    http.expectNone(contentUrl(6));
    active[0].flush(new Blob());
    const fifth = http.expectOne(contentUrl(5));
    http.expectNone(contentUrl(7));
    subscriptions[4].unsubscribe();
    expect(fifth.cancelled).toBe(false);
    duplicate.unsubscribe();
    expect(fifth.cancelled).toBe(true);
    http.expectOne(contentUrl(7)).flush(new Blob());
    active.slice(1).forEach((request) => request.flush(new Blob()));
  });

  it('cancels transport only after the last shared consumer leaves and frees the slot', () => {
    const first = coordinator.content(id(1)).subscribe();
    const second = coordinator.content(id(1)).subscribe();
    const request = http.expectOne(contentUrl(1));
    first.unsubscribe();
    expect(request.cancelled).toBe(false);
    second.unsubscribe();
    expect(request.cancelled).toBe(true);
    coordinator.content(id(1)).subscribe();
    http.expectOne(contentUrl(1)).flush(new Blob());
  });

  it('invalidates all active and queued work on account switch before accepting new work', () => {
    const next = vi.fn();
    for (let n = 1; n <= 6; n++) coordinator.content(id(n)).subscribe(next);
    const active = http.match(() => true);
    mock.emit('SIGNED_IN', createTestSession({ user: { ...createTestSession().user, id: 'user-2' }, access_token: 'second-token' }));
    coordinator.content(id(1)).subscribe();
    expect(active.every((request) => request.cancelled)).toBe(true);
    expect(next).not.toHaveBeenCalled();
    const request = http.expectOne(contentUrl(1));
    expect(request.request.headers.get('Authorization')).toBe('Bearer second-token');
    request.flush(new Blob());
    http.expectNone(contentUrl(5));
    http.expectNone(contentUrl(6));
  });

  it.each([200, 401, 404, 429, 503])('drops a late old-account %s before effects run', (status) => {
    const next = vi.fn();
    const error = vi.fn();
    coordinator.content(id(1)).subscribe({ next, error });
    const old = http.expectOne(contentUrl(1));
    const session = createTestSession({ user: { ...createTestSession().user, id: 'user-2' } });
    mock.emit('SIGNED_IN', session);
    old.flush(new Blob(), { status, statusText: 'Old response' });
    expect(next).not.toHaveBeenCalled();
    expect(error).not.toHaveBeenCalled();
    expect(auth.session()).toBe(session);
  });

  it('invalidates a same-user logout/login even with the same token and no effect flush in between', () => {
    const next = vi.fn();
    coordinator.content(id(1)).subscribe(next);
    const old = http.expectOne(contentUrl(1));
    mock.emit('SIGNED_OUT', null);
    mock.emit('SIGNED_IN', createTestSession());
    old.flush(new Blob());
    expect(next).not.toHaveBeenCalled();
    coordinator.content(id(1)).subscribe();
    http.expectOne(contentUrl(1)).flush(new Blob());
  });

  it('cancels on logout and issues no unauthenticated request', () => {
    coordinator.content(id(1)).subscribe();
    const request = http.expectOne(contentUrl(1));
    auth.clearLocalSession();
    TestBed.tick();
    expect(request.cancelled).toBe(true);
    const error = vi.fn();
    coordinator.content(id(1)).subscribe({ error });
    expect(error).toHaveBeenCalledOnce();
    http.expectNone(() => true);
  });

  it.each(['../escape', 'https://other.test/image', id(1) + '?token=x', 'not-a-uuid'])('rejects untrusted content identity %s before HTTP', (value) => {
    const error = vi.fn();
    coordinator.content(value).subscribe({ error });
    expect(error).toHaveBeenCalledOnce();
    http.expectNone(() => true);
  });
});
