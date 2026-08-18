import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';

import { HealthCheck } from './health-check';

describe('HealthCheck', () => {
  let fixture: ComponentFixture<HealthCheck>;
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HealthCheck],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(HealthCheck);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('calls the backend /health endpoint and shows success on ok', async () => {
    fixture.detectChanges();
    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/health'));
    req.flush({ status: 'ok' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('API healthy');
  });

  it('shows an error state when the health request fails', async () => {
    fixture.detectChanges();
    const req = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/health'));
    req.flush('Service Unavailable', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('API unreachable');
  });
});