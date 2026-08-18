import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SUPABASE_CLIENT } from '../../../core/auth/supabase-client';
import { createMockSupabase, createTestSession } from '../../../core/auth/testing/supabase-client.mock';

import { PlatformsList } from './platforms-list';

@Component({ template: '' })
class LoginStub {}

function configure(): { fixture: ComponentFixture<PlatformsList>; httpMock: HttpTestingController } {
  const mock = createMockSupabase(createTestSession());
  TestBed.configureTestingModule({
    imports: [PlatformsList],
    providers: [
      provideRouter([{ path: 'login', component: LoginStub }]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: SUPABASE_CLIENT, useValue: mock.client },
    ],
  });
  const fixture = TestBed.createComponent(PlatformsList);
  fixture.detectChanges();
  const httpMock = TestBed.inject(HttpTestingController);
  return { fixture, httpMock };
}

function listRequest(httpMock: HttpTestingController) {
  return httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/platforms'));
}

function buttonByText(fixture: ComponentFixture<PlatformsList>, text: string): HTMLButtonElement {
  const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
    (candidate) => candidate.textContent?.trim() === text,
  );
  if (!button) {
    throw new Error(`No button with text "${text}"`);
  }
  return button as HTMLButtonElement;
}

function setInputValue(fixture: ComponentFixture<PlatformsList>, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('PlatformsList', () => {
  let fixture: ComponentFixture<PlatformsList>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    const setup = configure();
    fixture = setup.fixture;
    httpMock = setup.httpMock;
  });

  afterEach(() => {
    httpMock.verify();
    vi.restoreAllMocks();
  });

  it('shows a loading state while the list request is pending', () => {
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Loading');

    listRequest(httpMock).flush([]);
  });

  it('shows the empty state when there are no platforms', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No platforms yet');
  });

  it('renders the returned platforms', async () => {
    listRequest(httpMock).flush([
      { id: '1', name: 'Steam' },
      { id: '2', name: 'Xbox' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Steam');
    expect(text).toContain('Xbox');
  });

  it('shows an error state with a retry action on non-401 failures', async () => {
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Unable to load your platforms');
    expect(text).toContain('Try again');
  });

  it('reloads the list when Try again is clicked', async () => {
    listRequest(httpMock).flush(null, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Try again').click();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Steam');
  });

  it('creates a platform on submit and reloads the list on success', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first platform').click();
    fixture.detectChanges();
    setInputValue(fixture, '  Steam  ');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/platforms'));
    expect(createReq.request.body).toEqual({ name: 'Steam' });
    createReq.flush({ id: '1', name: 'Steam' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Steam');
    expect(text).not.toContain('Create');
  });

  it('keeps the form open and shows a duplicate message on 409 create', async () => {
    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Add your first platform').click();
    fixture.detectChanges();
    setInputValue(fixture, 'Steam');
    buttonByText(fixture, 'Create').click();
    fixture.detectChanges();

    const createReq = httpMock.expectOne((request) => request.method === 'POST' && request.url.endsWith('/platforms'));
    createReq.flush({ title: 'Duplicate platform name' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('A platform with this name already exists.');
    expect((fixture.nativeElement as HTMLElement).querySelector('input')).not.toBeNull();
  });

  it('loads the current name, submits a rename, and reloads on success', async () => {
    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();

    const input = (fixture.nativeElement as HTMLElement).querySelector('input') as HTMLInputElement;
    expect(input.value).toBe('Steam');

    setInputValue(fixture, 'Nintendo Switch');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/platforms/1'));
    expect(updateReq.request.body).toEqual({ name: 'Nintendo Switch' });
    updateReq.flush({ id: '1', name: 'Nintendo Switch' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ id: '1', name: 'Nintendo Switch' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Nintendo Switch');
  });

  it('shows a not-found message and reloads on 404 rename', async () => {
    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Edit').click();
    fixture.detectChanges();
    setInputValue(fixture, 'Renamed');
    buttonByText(fixture, 'Save').click();
    fixture.detectChanges();

    const updateReq = httpMock.expectOne((request) => request.method === 'PUT' && request.url.endsWith('/platforms/1'));
    updateReq.flush(null, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('This platform no longer exists.');
  });

  it('confirms, deletes, and reloads the list on success', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    const deleteReq = httpMock.expectOne((request) => request.method === 'DELETE' && request.url.endsWith('/platforms/1'));
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    listRequest(httpMock).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No platforms yet');
  });

  it('does not delete when the confirmation is declined', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    httpMock.expectNone((request) => request.method === 'DELETE');
  });

  it('shows an in-use message on 409 delete', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    listRequest(httpMock).flush([{ id: '1', name: 'Steam' }]);
    await fixture.whenStable();
    fixture.detectChanges();

    buttonByText(fixture, 'Delete').click();
    fixture.detectChanges();

    const deleteReq = httpMock.expectOne((request) => request.method === 'DELETE' && request.url.endsWith('/platforms/1'));
    deleteReq.flush(null, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('This platform is in use and cannot be deleted.');
  });

  it('clears the session and navigates to /login on 401', async () => {
    const auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);

    listRequest(httpMock).flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(auth.isAuthenticated()).toBe(false);
    expect(router.url).toBe('/login');
  });
});