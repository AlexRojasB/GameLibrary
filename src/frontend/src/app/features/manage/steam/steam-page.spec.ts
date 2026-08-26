import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { environment } from '../../../../environments/environment';

import { SteamPage } from './steam-page';

describe('SteamPage', () => {
  let fixture: ComponentFixture<SteamPage>;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SteamPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    fixture = TestBed.createComponent(SteamPage);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('page load calls only GET /steam/status', () => {
    fixture.detectChanges();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`);
    expect(req.request.method).toBe('GET');
    expect(req.request.url).not.toContain('steampowered.com');
    req.flush(defaultStatus());
  });

  it('page load does not preview or import automatically', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());

    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it('renders disabled integration message', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush({
      enabled: false, linkAvailable: false, importAvailable: false,
      linked: false, steamId64: null, linkedAt: null,
      message: 'Steam integration is not configured.',
    });
    fixture.detectChanges();

    expect(text()).toContain('Steam integration is not configured');
  });

  it('renders import-unavailable message when Web API key is missing', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush({
      enabled: true, linkAvailable: true, importAvailable: false,
      linked: false, steamId64: null, linkedAt: null, message: null,
    });
    fixture.detectChanges();

    expect(text()).toContain('Steam library import is unavailable');
  });

  it('renders linked state with SteamID64', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    expect(text()).toContain('Linked Steam Account');
    expect(text()).toContain('76561198000000000');
    expect(text()).toContain('Unlink');
    expect(text()).toContain('Preview Steam library');
  });

  it('renders unlinked state with link button', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();

    expect(text()).toContain('No Steam Account Linked');
    expect(text()).toContain('Link Steam account');
  });

  it('link action calls POST /steam/link-requests and navigates to startUrl', () => {
    let capturedHref = '';
    const fakeLocation = {
      get href() { return capturedHref; },
      set href(val: string) { capturedHref = val; },
    } as Location;
    Object.defineProperty(window, 'location', { value: fakeLocation, writable: true, configurable: true });

    const startUrl = 'https://api.example.test/steam/link/start/abc123';

    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();

    clickButton('button');
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/link-requests`);
    expect(req.request.method).toBe('POST');
    req.flush({ startUrl, expiresAt: '2026-01-01T00:10:00Z' });

    expect(capturedHref).toBe(startUrl);
  });

  it('already-linked conflict renders unlink-first guidance', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();

    clickButton('button');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/link-requests`).flush(
      { detail: 'Unlink the current Steam account before linking another account.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    expect(text()).toContain('Unlink the current Steam account before linking another account.');
  });

  it('unlink action calls DELETE /steam/link and updates state', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('button.btn--danger');
    fixture.detectChanges();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/steam/link`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();

    expect(text()).toContain('No Steam Account Linked');
  });

  it('preview is explicit and has loading state', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    const btn = button('.btn--primary');
    expect(btn.textContent).toContain('Preview Steam library');

    btn.click();
    fixture.detectChanges();

    expect(btn.textContent).toContain('Loading');

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success', candidates: [], message: null,
    });
    fixture.detectChanges();

    expect(btn.textContent).toContain('Preview Steam library');
  });

  it('preview candidates render with selectable checkboxes', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success',
      candidates: [
        { steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: 123, alreadyImported: false },
        { steamAppId: 730, name: 'Counter-Strike 2', playtimeForeverMinutes: null, alreadyImported: true },
      ],
      message: null,
    });
    fixture.detectChanges();

    expect(text()).toContain('Dota 2');
    expect(text()).toContain('Counter-Strike 2');
    expect(text()).toContain('Already imported');
    const checkboxes = checkboxesAll();
    expect(checkboxes.length).toBe(2);
    expect(checkboxes[1].disabled).toBe(true);
  });

  it('import selected is disabled with no selection', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success',
      candidates: [{ steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: 123, alreadyImported: false }],
      message: null,
    });
    fixture.detectChanges();

    const importBtn = importButton();
    expect(importBtn.textContent).toContain('Import selected');
    expect(importBtn.disabled).toBe(true);
  });

  it('no-visible-games state renders privacy text', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'NoVisibleGames', candidates: [],
      message: 'Steam returned no visible games.',
    });
    fixture.detectChanges();

    expect(text()).toContain('Steam returned no visible games');
  });

  it('import selected calls POST with selected AppIDs only', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success',
      candidates: [
        { steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: false },
        { steamAppId: 730, name: 'CS2', playtimeForeverMinutes: null, alreadyImported: false },
        { steamAppId: 440, name: 'TF2', playtimeForeverMinutes: null, alreadyImported: true },
      ],
      message: null,
    });
    fixture.detectChanges();

    const cbs = checkboxesAll();
    cbs[0].click();
    cbs[1].click();
    fixture.detectChanges();

    importButton().click();
    fixture.detectChanges();

    const importReq = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`);
    expect(importReq.request.body).toEqual({ steamAppIds: [570, 730] });
    importReq.flush({
      imported: [
        { steamAppId: 570, videoGameId: '11111111-1111-1111-1111-111111111111', name: 'Dota 2' },
        { steamAppId: 730, videoGameId: '22222222-2222-2222-2222-222222222222', name: 'CS2' },
      ],
      alreadyImported: [440], unavailable: [], invalid: [],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    expect(text()).toContain('Import Summary');
    expect(text()).toContain('Imported: 2');
    expect(text()).toContain('Already imported: 1');
  });

  it('import loading has duplicate-click protection', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success',
      candidates: [{ steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: false }],
      message: null,
    });
    fixture.detectChanges();

    checkboxesAll()[0].click();
    fixture.detectChanges();

    const importBtn = importButton();
    importBtn.click();
    fixture.detectChanges();

    expect(importBtn.textContent).toContain('Importing');
    expect(importBtn.disabled).toBe(true);

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush({
      imported: [], alreadyImported: [], unavailable: [], invalid: [],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    expect(importBtn.disabled).toBe(false);
  });

  it('provider errors allow retry', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    clickButton('.btn--primary');
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success',
      candidates: [{ steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: false }],
      message: null,
    });
    fixture.detectChanges();

    checkboxesAll()[0].click();
    fixture.detectChanges();

    importButton().click();
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush(
      { detail: 'Steam did not respond in time. Please try again.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(text()).toContain('Steam did not respond in time');
  });

  it('401 shows error message', () => {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(
      { title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' },
    );
    fixture.detectChanges();

    expect(text()).toContain('Unable to load Steam status.');
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function button(selector: string): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLButtonElement;
  }

  function clickButton(selector: string): void {
    button(selector).click();
  }

  function importButton(): HTMLButtonElement {
    const btns = (fixture.nativeElement as HTMLElement).querySelectorAll('.btn--primary');
    return btns[btns.length - 1] as HTMLButtonElement;
  }

  function checkboxesAll(): HTMLInputElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.steam-candidate input[type="checkbox"]'));
  }

  function defaultStatus() {
    return {
      enabled: true, linkAvailable: true, importAvailable: true,
      linked: false, steamId64: null, linkedAt: null, message: null,
    };
  }

  function linkedStatus() {
    return {
      enabled: true, linkAvailable: true, importAvailable: true,
      linked: true, steamId64: '76561198000000000',
      linkedAt: '2026-01-01T00:00:00Z', message: null,
    };
  }
});
