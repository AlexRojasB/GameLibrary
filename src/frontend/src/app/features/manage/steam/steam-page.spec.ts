import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';

import { environment } from '../../../../environments/environment';

import { SteamPage } from './steam-page';
import { SteamPreviewCandidate } from './steam.service';

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

  it.each(['success', 'failure'])('keeps a volatile %s callback notice while removing only the callback query flag', (outcome) => {
    const route = TestBed.inject(ActivatedRoute);
    route.snapshot.queryParams = { steamLink: outcome, keep: 'yes' };
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();
    expect(text()).toContain(outcome === 'success' ? 'Steam linking completed' : 'Steam linking failed');
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true,
    });
    // The query flag is presentation only, not evidence that the account is linked.
    expect(text()).toContain('No Steam Account Linked');
    fixture.detectChanges();
    expect(fixture.componentInstance['callbackNotice']()).not.toBeNull();
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/link-requests`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it('ignores an unrecognized callback flag rather than rendering arbitrary query text', () => {
    TestBed.inject(ActivatedRoute).snapshot.queryParams = { steamLink: 'untrusted-query-text' };
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();
    expect(fixture.componentInstance['callbackNotice']()).toBeNull();
    expect(text()).not.toContain('untrusted-query-text');
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

  it('select all follows partial and full selection and excludes already-imported candidates', () => {
    loadPreview();

    expect(button('.btn--secondary').textContent).toContain('Select all');
    checkboxesAll()[0].click();
    fixture.detectChanges();
    expect(button('.btn--secondary').textContent).toContain('Select all');

    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(checkboxesAll().map((cb) => cb.checked)).toEqual([true, true, false]);
    expect(button('.btn--secondary').textContent).toContain('Deselect all');
    expect(importButton().disabled).toBe(false);

    checkboxesAll()[1].click();
    fixture.detectChanges();
    expect(button('.btn--secondary').textContent).toContain('Select all');
    checkboxesAll()[1].click();
    fixture.detectChanges();
    expect(button('.btn--secondary').textContent).toContain('Deselect all');

    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(checkboxesAll().every((cb) => !cb.checked)).toBe(true);
    expect(button('.btn--secondary').textContent).toContain('Select all');
    expect(importButton().disabled).toBe(true);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
  });

  it('cannot select imported or absent candidates through the UI or handlers', () => {
    loadPreview([
      { steamAppId: 440, name: 'TF2', playtimeForeverMinutes: null, alreadyImported: true },
    ]);

    checkboxesAll()[0].click();
    fixture.componentInstance['onToggle'](440);
    fixture.componentInstance['onToggle'](999);
    clickButton('.btn--secondary');
    fixture.detectChanges();

    expect(checkboxesAll()[0].disabled).toBe(true);
    expect(checkboxesAll()[0].checked).toBe(false);
    expect(fixture.componentInstance['selected']().size).toBe(0);
    expect(button('.btn--secondary').textContent).toContain('Select all');
    expect(importButton().disabled).toBe(true);
    fixture.componentInstance['onImport']();
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it('selects at most 100, preserves an explicit later choice, and never automatically imports another batch', () => {
    const candidates = Array.from({ length: 106 }, (_, i) => ({
      steamAppId: 1000 + i, name: `Game ${i}`, playtimeForeverMinutes: null, alreadyImported: i === 0,
    }));
    loadPreview(candidates);
    checkboxesAll()[105].click();
    fixture.detectChanges();
    expect(button('.btn--secondary').textContent).toContain('Select up to 100');
    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(fixture.componentInstance['selectedCount']()).toBe(100);
    expect(checkboxesAll()[105].checked).toBe(true);
    expect(checkboxesAll()[0].checked).toBe(false);
    expect(text()).toContain('100 selected');
    expect(text()).toContain('Select at most 100 games per import');
    expect(button('.btn--secondary').textContent).toContain('Deselect all');
    const unselected = checkboxesAll().findIndex((cb, i) => i > 0 && !cb.checked);
    expect(checkboxesAll()[unselected].disabled).toBe(true);
    fixture.componentInstance['onToggle'](candidates[unselected].steamAppId);
    expect(fixture.componentInstance['selectedCount']()).toBe(100);
    checkboxesAll()[1].click();
    fixture.detectChanges();
    expect(checkboxesAll()[unselected].disabled).toBe(false);
    checkboxesAll()[unselected].click();
    fixture.detectChanges();
    importButton().click();
    const request = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`);
    expect(request.request.body.steamAppIds).toHaveLength(100);
    expect(new Set(request.request.body.steamAppIds).size).toBe(100);
    expect(request.request.body.steamAppIds).not.toContain(1000);
    request.flush({ imported: [], alreadyImported: [], unavailable: [], invalid: [] });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();
    expect(fixture.componentInstance['selectedCount']()).toBe(0);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it('deselects a capped batch and guards an oversized selection even through the import handler', () => {
    loadPreview(Array.from({ length: 101 }, (_, i) => ({
      steamAppId: i + 1, name: `Game ${i}`, playtimeForeverMinutes: null, alreadyImported: false,
    })));
    clickButton('.btn--secondary'); fixture.detectChanges();
    expect(fixture.componentInstance['selectedCount']()).toBe(100);
    clickButton('.btn--secondary'); fixture.detectChanges();
    expect(fixture.componentInstance['selectedCount']()).toBe(0);
    fixture.componentInstance['selected'].set(new Set(Array.from({ length: 101 }, (_, i) => i + 1)));
    fixture.detectChanges();
    expect(importButton().disabled).toBe(true);
    fixture.componentInstance['onImport']();
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
  });

  it.each([
    { state: 'Success', candidates: [
      { steamAppId: 10, name: 'New candidate', playtimeForeverMinutes: null, alreadyImported: false },
    ] },
    { state: 'Success', candidates: [] },
    { state: 'NoVisibleGames', candidates: [] },
  ])('preview resets selection-derived state for $state with $candidates.length candidates', (response) => {
    loadPreview();
    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(true);

    clickButton('.steam-card__actions .btn--primary');
    fixture.detectChanges();
    expect(fixture.componentInstance['hasSelectedSelectable']()).toBe(false);
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(false);
    expect(button('.btn--secondary').disabled).toBe(true);
    expect(checkboxesAll().every((cb) => !cb.checked && cb.disabled)).toBe(true);
    expect(importButton().disabled).toBe(true);

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({ ...response, message: null });
    fixture.detectChanges();
    expect(fixture.componentInstance['hasSelectedSelectable']()).toBe(false);
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(false);
    if (response.candidates.length > 0) {
      expect(button('.btn--secondary').textContent).toContain('Select all');
      expect(importButton().disabled).toBe(true);
      expect(checkboxesAll()[0].checked).toBe(false);
    } else {
      expect(checkboxesAll()).toHaveLength(0);
    }
  });

  it('guards duplicate previews and blocks import, selection and unlink while preview is pending', () => {
    loadPreview();
    const previewButton = button('.steam-card__actions .btn--primary');
    previewButton.click();
    previewButton.click();
    fixture.componentInstance['onPreview']();
    fixture.componentInstance['onToggle'](570);
    fixture.componentInstance['onToggleAll']();
    fixture.componentInstance['onImport']();
    fixture.componentInstance['onUnlink']();
    fixture.detectChanges();

    expect(fixture.componentInstance['selected']().size).toBe(0);
    expect(previewButton.disabled).toBe(true);
    expect(button('.btn--danger').disabled).toBe(true);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/link`);
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success', candidates: [], message: null,
    });
    fixture.detectChanges();
    expect(previewButton.disabled).toBe(false);
    expect(button('.btn--danger').disabled).toBe(false);
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
    expect(text()).toContain('check Steam Privacy Settings');
    expect(text()).toContain('No games were imported');
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
    expect(checkboxesAll().every((cb) => cb.disabled && !cb.checked)).toBe(true);
    expect(importButton().disabled).toBe(true);
    expect(button('.btn--secondary').textContent).toContain('Select all');
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(false);
    fixture.componentInstance['onToggle'](570);
    fixture.componentInstance['onToggleAll']();
    fixture.componentInstance['onImport']();
    expect(fixture.componentInstance['selected']().size).toBe(0);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
  });

  it('disables imported and alreadyImported results but leaves unavailable and invalid candidates selectable', () => {
    loadPreview([
      { steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: false },
      { steamAppId: 730, name: 'CS2', playtimeForeverMinutes: null, alreadyImported: false },
      { steamAppId: 10, name: 'Unavailable', playtimeForeverMinutes: null, alreadyImported: false },
      { steamAppId: 20, name: 'Invalid', playtimeForeverMinutes: null, alreadyImported: false },
      { steamAppId: 440, name: 'TF2', playtimeForeverMinutes: null, alreadyImported: true },
    ]);
    clickButton('.btn--secondary');
    fixture.detectChanges();
    importButton().click();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush({
      imported: [{ steamAppId: 570, videoGameId: 'game-570', name: 'Dota 2' }],
      alreadyImported: [730], unavailable: [10], invalid: [20],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    expect(checkboxesAll().map((cb) => cb.disabled)).toEqual([true, true, false, false, true]);
    expect(checkboxesAll().every((cb) => !cb.checked)).toBe(true);
    expect(importButton().disabled).toBe(true);
    expect(button('.btn--secondary').textContent).toContain('Select all');
    expect(text()).toContain('Imported: 1');
    expect(text()).toContain('Already imported: 1');
    expect(text()).toContain('Unavailable: 1');
    expect(text()).toContain('Invalid: 1');

    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(checkboxesAll().map((cb) => cb.checked)).toEqual([false, false, true, true, false]);
    importButton().click();
    const retry = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`);
    expect(retry.request.body).toEqual({ steamAppIds: [10, 20] });
    retry.flush({ imported: [], alreadyImported: [], unavailable: [10], invalid: [20] });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
  });

  it('re-preview after import clears the summary and uses fresh candidate flags with no selection', () => {
    loadPreview();
    checkboxesAll()[0].click();
    fixture.detectChanges();
    importButton().click();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush({
      imported: [{ steamAppId: 570, videoGameId: 'game-570', name: 'Dota 2' }],
      alreadyImported: [], unavailable: [], invalid: [],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();
    expect(text()).toContain('Import Summary');

    clickButton('.steam-card__actions .btn--primary');
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success', message: null,
      candidates: [
        { steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: true },
        { steamAppId: 730, name: 'CS2', playtimeForeverMinutes: null, alreadyImported: false },
      ],
    });
    fixture.detectChanges();
    expect(text()).not.toContain('Import Summary');
    expect(checkboxesAll().map((cb) => cb.disabled)).toEqual([true, false]);
    expect(checkboxesAll().every((cb) => !cb.checked)).toBe(true);
    expect(importButton().disabled).toBe(true);
    expect(button('.btn--secondary').textContent).toContain('Select all');
  });

  it('unlink clears preview, summary and selection-derived state and blocks overlapping actions', () => {
    loadPreview();
    checkboxesAll()[0].click();
    fixture.detectChanges();
    importButton().click();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush({
      imported: [{ steamAppId: 570, videoGameId: 'game-570', name: 'Dota 2' }],
      alreadyImported: [], unavailable: [], invalid: [],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();
    clickButton('.btn--secondary');
    fixture.detectChanges();
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(true);

    clickButton('.btn--danger');
    fixture.componentInstance['onUnlink']();
    fixture.componentInstance['onPreview']();
    fixture.componentInstance['onImport']();
    fixture.componentInstance['onToggle'](730);
    fixture.componentInstance['onToggleAll']();
    fixture.detectChanges();
    expect(button('.steam-card__actions .btn--primary').disabled).toBe(true);
    expect(importButton().disabled).toBe(true);
    expect(button('.btn--secondary').disabled).toBe(true);
    expect(checkboxesAll().every((cb) => cb.disabled)).toBe(true);
    expect([...fixture.componentInstance['selected']()]).toEqual([730]);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/import`);
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/link`).flush(null);
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(defaultStatus());
    fixture.detectChanges();

    expect(text()).toContain('No Steam Account Linked');
    expect(text()).not.toContain('Steam Library Preview');
    expect(text()).not.toContain('Import Summary');
    expect(fixture.componentInstance['selected']().size).toBe(0);
    expect(fixture.componentInstance['hasSelectedSelectable']()).toBe(false);
    expect(fixture.componentInstance['allSelectableSelected']()).toBe(false);
    expect(fixture.componentInstance['previewState']()).toBeNull();
    expect(fixture.componentInstance['previewMessage']()).toBeNull();
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
    importBtn.click();
    fixture.componentInstance['onImport']();
    fixture.componentInstance['onPreview']();
    fixture.componentInstance['onUnlink']();
    fixture.componentInstance['onToggle'](570);
    fixture.componentInstance['onToggleAll']();
    fixture.detectChanges();

    expect(importBtn.textContent).toContain('Importing');
    expect(importBtn.disabled).toBe(true);
    expect(checkboxesAll()[0].checked).toBe(true);
    expect(checkboxesAll()[0].disabled).toBe(true);
    expect(button('.btn--secondary').disabled).toBe(true);
    expect(button('.steam-card__actions .btn--primary').disabled).toBe(true);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/library/preview`);
    httpMock.expectNone(`${environment.apiBaseUrl}/steam/link`);

    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`).flush({
      imported: [], alreadyImported: [], unavailable: [], invalid: [],
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();

    expect(importBtn.disabled).toBe(true);
    expect(checkboxesAll()[0].checked).toBe(false);
    expect(checkboxesAll()[0].disabled).toBe(false);
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
    expect(checkboxesAll()[0].checked).toBe(true);
    expect(checkboxesAll()[0].disabled).toBe(false);
    expect(importButton().disabled).toBe(false);
    expect(button('.btn--secondary').textContent).toContain('Deselect all');

    importButton().click();
    fixture.detectChanges();
    expect(text()).not.toContain('Steam did not respond in time');
    const retry = httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/import`);
    expect(retry.request.body).toEqual({ steamAppIds: [570] });
    retry.flush({ imported: [], alreadyImported: [570], unavailable: [], invalid: [] });
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();
    expect(checkboxesAll()[0].disabled).toBe(true);
    expect(importButton().disabled).toBe(true);
  });

  it('preview errors release pending controls and allow an explicit retry', () => {
    loadPreview();
    clickButton('.btn--secondary');
    fixture.detectChanges();
    clickButton('.steam-card__actions .btn--primary');
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush(
      { detail: 'Steam did not respond in time. Please try again.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();

    expect(text()).toContain('Steam did not respond in time');
    expect(button('.steam-card__actions .btn--primary').disabled).toBe(false);
    expect(button('.btn--secondary').disabled).toBe(false);
    expect(button('.btn--secondary').textContent).toContain('Select all');
    expect(importButton().disabled).toBe(true);
    expect(checkboxesAll().every((cb) => !cb.checked)).toBe(true);

    clickButton('.steam-card__actions .btn--primary');
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'NoVisibleGames', candidates: [], message: null,
    });
    fixture.detectChanges();
    expect(text()).not.toContain('Steam did not respond in time');
    expect(text()).toContain('Your Steam game details may be private');
    expect(checkboxesAll()).toHaveLength(0);
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

  function loadPreview(candidates: SteamPreviewCandidate[] = [
    { steamAppId: 570, name: 'Dota 2', playtimeForeverMinutes: null, alreadyImported: false },
    { steamAppId: 730, name: 'CS2', playtimeForeverMinutes: null, alreadyImported: false },
    { steamAppId: 440, name: 'TF2', playtimeForeverMinutes: null, alreadyImported: true },
  ]): void {
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/status`).flush(linkedStatus());
    fixture.detectChanges();
    clickButton('.steam-card__actions .btn--primary');
    httpMock.expectOne(`${environment.apiBaseUrl}/steam/library/preview`).flush({
      state: 'Success', candidates, message: null,
    });
    fixture.detectChanges();
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
