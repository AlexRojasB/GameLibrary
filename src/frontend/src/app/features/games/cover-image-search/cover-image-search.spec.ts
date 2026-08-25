import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { environment } from '../../../../environments/environment';

import { CoverImageSearch } from './cover-image-search';

describe('CoverImageSearch', () => {
  let fixture: ComponentFixture<CoverImageSearch>;
  let httpMock: HttpTestingController;

  function configure(inputs: { gameType?: 'VideoGame' | 'BoardGame'; gameName?: string; coverImageUrl?: string | null; platforms?: string[] } = {}): void {
    TestBed.configureTestingModule({
      imports: [CoverImageSearch],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(CoverImageSearch);
    fixture.componentRef.setInput('gameType', inputs.gameType ?? 'VideoGame');
    fixture.componentRef.setInput('gameName', inputs.gameName ?? 'Hades');
    fixture.componentRef.setInput('coverImageUrl', inputs.coverImageUrl ?? null);
    fixture.componentRef.setInput('selectedPlatformNames', inputs.platforms ?? []);
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);
  }

  function searchButton(): HTMLButtonElement {
    return (fixture.nativeElement as HTMLElement).querySelector('.cover-search__heading button') as HTMLButtonElement;
  }

  function clickSearch(): void {
    searchButton().click();
    fixture.detectChanges();
  }

  function expectSearchRequest() {
    return httpMock.expectOne(`${environment.apiBaseUrl}/cover-images/search`);
  }

  function flushCandidates(count = 1): void {
    const req = expectSearchRequest();
    req.flush({
      candidates: Array.from({ length: count }, (_value, index) => ({
        imageUrl: `https://example.com/${index + 1}.jpg`,
        thumbnailUrl: `https://example.com/${index + 1}-thumb.jpg`,
        sourcePageUrl: `https://source.example/${index + 1}`,
        sourceName: `Source ${index + 1}`,
        width: 600,
        height: 900,
      })),
    });
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('does not search while typing a name', () => {
    configure({ gameName: '' });

    fixture.componentRef.setInput('gameName', 'Resident Evil 4');
    fixture.detectChanges();

    httpMock.expectNone(`${environment.apiBaseUrl}/cover-images/search`);
  });

  it('does not search when Platforms change', () => {
    configure({ platforms: ['Switch'] });

    fixture.componentRef.setInput('selectedPlatformNames', ['Switch', 'Steam']);
    fixture.detectChanges();

    httpMock.expectNone(`${environment.apiBaseUrl}/cover-images/search`);
  });

  it('makes one request after explicit Search cover activation and shows loading', () => {
    configure({ gameName: 'Resident Evil 4', platforms: ['PlayStation 5'] });

    clickSearch();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Searching cover images...');
    const req = expectSearchRequest();
    expect(req.request.body).toEqual({ gameType: 'VideoGame', name: 'Resident Evil 4', platformName: 'PlayStation 5' });
    req.flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('prevents duplicate pending search requests', () => {
    configure();

    clickSearch();
    searchButton().click();

    expectSearchRequest().flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('renders at most five visual cards', () => {
    configure();

    clickSearch();
    flushCandidates(6);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(5);
  });

  it('selecting a candidate updates CoverImageUrl output and selected marker', () => {
    configure();
    const selected: string[] = [];
    fixture.componentInstance.coverImageUrlSelected.subscribe((value) => selected.push(value));

    clickSearch();
    flushCandidates(2);
    ((fixture.nativeElement as HTMLElement).querySelector('.cover-search-card') as HTMLButtonElement).click();
    fixture.componentRef.setInput('coverImageUrl', selected[0]);
    fixture.detectChanges();

    expect(selected).toEqual(['https://example.com/1.jpg']);
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Selected');
  });

  it('manual URL modification clears selected marker while preserving the gallery', () => {
    configure();
    const selected: string[] = [];
    fixture.componentInstance.coverImageUrlSelected.subscribe((value) => selected.push(value));

    clickSearch();
    flushCandidates(2);
    ((fixture.nativeElement as HTMLElement).querySelector('.cover-search-card') as HTMLButtonElement).click();
    fixture.componentRef.setInput('coverImageUrl', selected[0]);
    fixture.detectChanges();

    fixture.componentRef.setInput('coverImageUrl', 'https://manual.example/cover.jpg');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(2);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Selected');
  });

  it('shows zero results as a non-error state', () => {
    configure();

    clickSearch();
    flushCandidates(0);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('No cover images found.');
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();
  });

  it('shows provider errors and allows retry', () => {
    configure();

    clickSearch();
    expectSearchRequest().flush(
      { title: 'Cover image search unavailable', detail: 'Unable to search cover images right now.' },
      { status: 503, statusText: 'Service Unavailable' },
    );
    fixture.detectChanges();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Unable to search cover images right now.');

    clickSearch();
    flushCandidates(1);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(1);
  });

  it('emits unauthorized on a 401 search response', () => {
    configure();
    let unauthorized = false;
    fixture.componentInstance.unauthorized.subscribe(() => (unauthorized = true));

    clickSearch();
    expectSearchRequest().flush({}, { status: 401, statusText: 'Unauthorized' });
    fixture.detectChanges();

    expect(unauthorized).toBe(true);
    expect((fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')).toBeNull();
  });

  it('keeps broken preview layout and disables that candidate', () => {
    configure();

    clickSearch();
    flushCandidates(1);
    const image = (fixture.nativeElement as HTMLElement).querySelector('img') as HTMLImageElement;
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();

    const card = (fixture.nativeElement as HTMLElement).querySelector('.cover-search-card') as HTMLButtonElement;
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Preview unavailable');
    expect(card.disabled).toBe(true);
  });

  it('uses multiple-platform selector order and selected context', () => {
    configure({ gameName: 'Hades', platforms: ['Xbox', 'Steam'] });

    const select = (fixture.nativeElement as HTMLElement).querySelector('#cover-search-platform') as HTMLSelectElement;
    expect([...select.options].map((option) => option.textContent?.trim())).toEqual(['Xbox', 'Steam']);

    select.selectedIndex = 1;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    clickSearch();

    const req = expectSearchRequest();
    expect(req.request.body).toEqual({ gameType: 'VideoGame', name: 'Hades', platformName: 'Steam' });
    req.flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('sends no platformName for zero-platform VideoGame search', () => {
    configure({ platforms: [] });

    clickSearch();

    const req = expectSearchRequest();
    expect(req.request.body).toEqual({ gameType: 'VideoGame', name: 'Hades' });
    req.flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('uses one VideoGame platform automatically', () => {
    configure({ platforms: ['Switch'] });

    clickSearch();

    const req = expectSearchRequest();
    expect(req.request.body).toEqual({ gameType: 'VideoGame', name: 'Hades', platformName: 'Switch' });
    req.flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('BoardGame search sends no platform context', () => {
    configure({ gameType: 'BoardGame', gameName: 'Catan', platforms: ['Ignored'] });

    clickSearch();

    const req = expectSearchRequest();
    expect(req.request.body).toEqual({ gameType: 'BoardGame', name: 'Catan' });
    req.flush({ candidates: [] });
    fixture.detectChanges();
  });

  it('name changes clear stale gallery and selected marker without changing selected URL value', () => {
    configure({ gameName: 'Resident Evil 4' });
    let coverImageUrl = '';
    fixture.componentInstance.coverImageUrlSelected.subscribe((value) => (coverImageUrl = value));

    clickSearch();
    flushCandidates(1);
    ((fixture.nativeElement as HTMLElement).querySelector('.cover-search-card') as HTMLButtonElement).click();
    fixture.componentRef.setInput('coverImageUrl', coverImageUrl);
    fixture.detectChanges();

    fixture.componentRef.setInput('gameName', 'Resident Evil 5');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(0);
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Selected');
    expect(coverImageUrl).toBe('https://example.com/1.jpg');
    httpMock.expectNone(`${environment.apiBaseUrl}/cover-images/search`);
  });

  it('platform context changes clear stale gallery', () => {
    configure({ platforms: ['Switch'] });

    clickSearch();
    flushCandidates(1);
    fixture.componentRef.setInput('selectedPlatformNames', ['Steam']);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(0);
    httpMock.expectNone(`${environment.apiBaseUrl}/cover-images/search`);
  });

  it('obsolete in-flight response cannot repopulate the gallery', () => {
    configure({ gameName: 'A' });

    clickSearch();
    const req = expectSearchRequest();
    fixture.componentRef.setInput('gameName', 'B');
    fixture.detectChanges();

    req.flush({
      candidates: [
        {
          imageUrl: 'https://example.com/stale.jpg',
          thumbnailUrl: null,
          sourcePageUrl: null,
          sourceName: null,
          width: null,
          height: null,
        },
      ],
    });
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.cover-search-card').length).toBe(0);
  });
});
