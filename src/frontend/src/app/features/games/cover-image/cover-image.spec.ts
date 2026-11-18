import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';
import { API_BASE_URL, apiTokenInterceptor } from '../../../core/auth/token.interceptor';

import { CoverImage } from './cover-image';
import { mockCoverVisibility } from './cover-image.testing';

const API = 'https://api.example.test';
const ID = '00000000-0000-0000-0000-000000000001';
const OTHER = '00000000-0000-0000-0000-000000000002';
const contentUrl = (id = ID) => `${API}/cover-uploads/${id}/content`;

describe('CoverImage', () => {
  let fixtures: ComponentFixture<CoverImage>[];
  let http: HttpTestingController;
  let mock: ReturnType<typeof createMockSupabase>;
  let observers: ReturnType<typeof mockCoverVisibility>;
  let createUrl: ReturnType<typeof vi.fn<(blob: Blob | MediaSource) => string>>;
  let revokeUrl: ReturnType<typeof vi.fn<(url: string) => void>>;

  beforeEach(async () => {
    fixtures = [];
    observers = mockCoverVisibility();
    let serial = 0;
    createUrl = vi.fn(() => `blob:cover-${++serial}`);
    revokeUrl = vi.fn();
    vi.stubGlobal('URL', class extends URL {
      static override createObjectURL = createUrl;
      static override revokeObjectURL = revokeUrl;
    });
    mock = createMockSupabase(createTestSession());
    TestBed.configureTestingModule({ imports: [CoverImage], providers: [
      provideHttpClient(withInterceptors([apiTokenInterceptor])), provideHttpClientTesting(), provideRouter([]),
      { provide: API_BASE_URL, useValue: API },
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ] });
    await TestBed.inject(AuthService).initialize();
    vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    fixtures.forEach((fixture) => fixture.destroy());
    http.verify();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  function render(uploadId: string | null = ID, external: string | null = null): ComponentFixture<CoverImage> {
    const fixture = TestBed.createComponent(CoverImage);
    fixtures.push(fixture);
    fixture.componentRef.setInput('coverUploadId', uploadId);
    fixture.componentRef.setInput('coverImageUrl', external);
    fixture.componentRef.setInput('alt', 'Hades cover');
    fixture.componentRef.setInput('imageClass', 'library-card__cover');
    fixture.detectChanges();
    return fixture;
  }

  function show(index = 0, visible = true): void {
    observers[index].show(visible);
    TestBed.tick();
  }

  function image(fixture: ComponentFixture<CoverImage>): HTMLImageElement | null {
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('img');
  }

  function retry(fixture: ComponentFixture<CoverImage>): void {
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement).querySelector('button')!.click();
    fixture.detectChanges();
  }

  it('waits for near visibility, cancels on leaving, and disconnects on destroy', () => {
    const fixture = render();
    expect(observers[0].options.rootMargin).toBe('200px');
    expect(observers[0].target).toBe(fixture.nativeElement);
    http.expectNone(contentUrl());
    show();
    expect(fixture.nativeElement.textContent).toContain('Loading cover...');
    const pending = http.expectOne(contentUrl());
    show(0, false);
    expect(pending.cancelled).toBe(true);
    show();
    const restarted = http.expectOne(contentUrl());
    fixture.destroy();
    expect(restarted.cancelled).toBe(true);
    expect(observers[0].disconnect).toHaveBeenCalledOnce();
  });

  it('fails closed without IntersectionObserver rather than fetching the entire shelf', () => {
    vi.stubGlobal('IntersectionObserver', undefined);
    const fixture = render();
    expect(image(fixture)).toBeNull();
    http.expectNone(() => true);
  });

  it('renders direct external images without any HttpClient request or Bearer token', () => {
    const fixture = render(null, 'https://external.example.test/hades.jpg');
    expect(image(fixture)?.getAttribute('src')).toBe('https://external.example.test/hades.jpg');
    expect(image(fixture)?.alt).toBe('Hades cover');
    expect(image(fixture)?.classList.contains('library-card__cover')).toBe(true);
    show();
    http.expectNone(() => true);
    image(fixture)!.dispatchEvent(new Event('error'));
    expect(image(fixture)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Cover unavailable');
    show(0, false);
    show();
    expect(image(fixture)).toBeNull();
    retry(fixture);
    expect(image(fixture)?.getAttribute('src')).toBe('https://external.example.test/hades.jpg');
    http.expectNone(() => true);
  });

  it('creates separate component URLs from one in-flight Blob and revokes only the departing component', () => {
    const first = render();
    const second = render();
    show(0);
    show(1);
    const request = http.expectOne(contentUrl());
    expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
    const blob = new Blob(['jpeg'], { type: 'image/jpeg' });
    request.flush(blob);
    expect(image(first)?.getAttribute('src')).toBe('blob:cover-1');
    expect(image(second)?.getAttribute('src')).toBe('blob:cover-2');
    expect(createUrl.mock.calls).toEqual([[blob], [blob]]);
    first.destroy();
    expect(revokeUrl.mock.calls).toEqual([['blob:cover-1']]);
    expect(image(second)?.getAttribute('src')).toBe('blob:cover-2');
    second.destroy();
    expect(revokeUrl.mock.calls).toEqual([['blob:cover-1'], ['blob:cover-2']]);
  });

  it('keeps only its local completed image while offscreen and never shares it with a new component', () => {
    const first = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob());
    show(0, false);
    show(0);
    expect(image(first)?.getAttribute('src')).toBe('blob:cover-1');
    http.expectNone(contentUrl());
    render();
    show(1);
    http.expectOne(contentUrl()).flush(new Blob());
  });

  it('removes a queued offscreen component without dispatching its content after a slot opens', () => {
    for (let n = 1; n <= 5; n++) {
      render(`00000000-0000-0000-0000-${String(n).padStart(12, '0')}`);
      show(n - 1);
    }
    const active = http.match(() => true);
    expect(active).toHaveLength(4);
    show(4, false);
    active.forEach((request) => request.flush(new Blob()));
    http.expectNone(() => true);
  });

  it('revokes on managed/external/empty source changes without a hidden fallback', () => {
    const fixture = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob());
    fixture.componentRef.setInput('coverUploadId', OTHER);
    fixture.detectChanges();
    expect(revokeUrl).toHaveBeenCalledExactlyOnceWith('blob:cover-1');
    expect(image(fixture)).toBeNull();
    http.expectOne(contentUrl(OTHER)).flush(new Blob());
    fixture.componentRef.setInput('coverUploadId', null);
    fixture.componentRef.setInput('coverImageUrl', 'https://external.test/new.jpg');
    fixture.detectChanges();
    expect(revokeUrl).toHaveBeenCalledWith('blob:cover-2');
    expect(image(fixture)?.getAttribute('src')).toBe('https://external.test/new.jpg');
    fixture.componentRef.setInput('coverImageUrl', null);
    expect(image(fixture)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('No cover');
  });

  it.each([200, 401, 404, 429, 503])('ignores a stale source %s arriving before the input effect runs', (status) => {
    const fixture = render();
    show();
    const pending = http.expectOne(contentUrl());
    fixture.componentRef.setInput('coverUploadId', OTHER);
    pending.flush(new Blob(), { status, statusText: 'Old response' });
    fixture.detectChanges();
    expect(createUrl).not.toHaveBeenCalled();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(TestBed.inject(Router).navigateByUrl).not.toHaveBeenCalled();
    http.expectOne(contentUrl(OTHER)).flush(new Blob());
    expect(image(fixture)?.getAttribute('src')).toBe('blob:cover-1');
  });

  it('cancels active source work and disposes a Blob immediately on image error, then retries explicitly', () => {
    const fixture = render();
    show();
    const old = http.expectOne(contentUrl());
    fixture.componentRef.setInput('coverUploadId', OTHER);
    fixture.detectChanges();
    expect(old.cancelled).toBe(true);
    http.expectOne(contentUrl(OTHER)).flush(new Blob());
    image(fixture)!.dispatchEvent(new Event('error'));
    expect(image(fixture)).toBeNull();
    expect(revokeUrl).toHaveBeenCalledExactlyOnceWith('blob:cover-1');
    http.expectNone(() => true);
    retry(fixture);
    http.expectOne(contentUrl(OTHER)).flush(new Blob());
    expect(image(fixture)?.getAttribute('src')).toBe('blob:cover-2');
  });

  it.each([404, 429, 503])('shows a placeholder and explicit retry without polling on %s', (status) => {
    const fixture = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob(), { status, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(image(fixture)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Cover unavailable');
    show(0, false);
    show();
    http.expectNone(() => true);
    retry(fixture);
    http.expectOne(contentUrl()).flush(new Blob());
    expect(image(fixture)).not.toBeNull();
  });

  it.each(['5', 'Fri, 30 Oct 2026 12:00:05 GMT'])('honors Retry-After %s only on explicit retry, with no timer or polling', (retryAfter) => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-30T12:00:00Z'));
    const fixture = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob(), { status: 429, statusText: 'Busy', headers: { 'Retry-After': retryAfter } });
    retry(fixture);
    http.expectNone(() => true);
    expect(fixture.nativeElement.textContent).toContain('Retry available after');
    expect(vi.getTimerCount()).toBe(0);
    vi.setSystemTime(new Date('2026-10-30T12:00:06Z'));
    http.expectNone(() => true);
    retry(fixture);
    http.expectOne(contentUrl()).flush(new Blob());
  });

  it('clears auth and uses the normal login navigation on a current 401', () => {
    const fixture = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob(), { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(false);
    expect(TestBed.inject(Router).navigateByUrl).toHaveBeenCalledExactlyOnceWith('/login');
    expect(image(fixture)).toBeNull();
  });

  it('revokes on logout, requires fresh authorization after same-user login and on account switch', () => {
    const fixture = render();
    show();
    http.expectOne(contentUrl()).flush(new Blob());
    mock.emit('SIGNED_OUT', null);
    fixture.detectChanges();
    expect(revokeUrl).toHaveBeenCalledWith('blob:cover-1');
    expect(image(fixture)).toBeNull();
    mock.emit('SIGNED_IN', createTestSession());
    fixture.detectChanges();
    http.expectOne(contentUrl()).flush(new Blob());
    expect(image(fixture)?.getAttribute('src')).toBe('blob:cover-2');
    const secondSession = createTestSession({ user: { ...createTestSession().user, id: 'user-2' } });
    mock.emit('SIGNED_IN', secondSession);
    fixture.detectChanges();
    expect(revokeUrl).toHaveBeenCalledWith('blob:cover-2');
    const secondRequest = http.expectOne(contentUrl());
    mock.emit('SIGNED_OUT', null);
    mock.emit('SIGNED_IN', createTestSession());
    secondRequest.flush(new Blob(), { status: 401, statusText: 'Old account' });
    fixture.detectChanges();
    expect(TestBed.inject(AuthService).isAuthenticated()).toBe(true);
    expect(TestBed.inject(Router).navigateByUrl).not.toHaveBeenCalled();
    http.expectOne(contentUrl()).flush(new Blob());
  });
});
