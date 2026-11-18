import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AuthService } from '../../../../core/auth/auth.service';
import { createTestSession } from '../../../../core/auth/testing/supabase-client.mock';

import { BoardGame, BoardGameInput } from '../../board-game';

import { BoardGameForm } from './board-game-form';
import { CoverImageSearch } from '../../cover-image-search/cover-image-search';

const ownedGame: BoardGame = {
  id: '1',
  name: 'Catan',
  coverImageUrl: null,
  coverUploadId: null,
  isFamilyFriendly: false,
  minimumPlayers: 3,
  maximumPlayers: 4,
  approximateDuration: 60,
  interactionType: 'Cooperative',
  acquisitionStatus: 'Owned',
  rating: 5,
  notes: 'Trading',
  createdAt: '2026-08-17T00:00:00Z',
};

describe('BoardGameForm', () => {
  let fixture: ComponentFixture<BoardGameForm>;
  let httpMock: HttpTestingController;

  function configure(game: BoardGame | null = null): void {
    TestBed.configureTestingModule({
      imports: [BoardGameForm],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
        { provide: AuthService, useValue: { session: signal(createTestSession()), clearLocalSession: vi.fn() } }],
    });
    fixture = TestBed.createComponent(BoardGameForm);
    fixture.componentRef.setInput('game', game);
    fixture.componentRef.setInput('submitLabel', 'Save');
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);
    httpMock.expectOne((request) => request.url.endsWith('/cover-uploads/options')).flush({ enabled: false });
  }

  function collectSubmitted(): BoardGameInput[] {
    const emitted: BoardGameInput[] = [];
    fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));
    return emitted;
  }

  function setName(value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-name') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function setNumber(selector: string, value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(): void {
    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (candidate) => candidate.textContent?.trim() === 'Save',
    ) as HTMLButtonElement;
    button.click();
    fixture.detectChanges();
  }

  function selectOption(selector: string, text: string): void {
    const select = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLSelectElement;
    const index = [...select.options].findIndex((option) => option.textContent?.trim() === text);
    select.selectedIndex = index;
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('quick-adds an Owned board game with the approved default interaction type', () => {
    configure();
    const emitted = collectSubmitted();
    const familyFriendly = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-family-friendly') as HTMLInputElement;
    expect(familyFriendly.checked).toBe(false);
    expect(familyFriendly.labels?.[0].textContent?.trim()).toBe('Family friendly');
    expect((fixture.nativeElement as HTMLElement).querySelector(`#${familyFriendly.getAttribute('aria-describedby')}`)?.textContent)
      .toContain('appropriate for family play');

    setName('  Catan  ');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');
    submit();

    expect(emitted).toEqual([
      {
        name: 'Catan',
        isFamilyFriendly: false,
        minimumPlayers: 3,
        maximumPlayers: 4,
        approximateDuration: null,
        interactionType: 'Competitive',
        acquisitionStatus: 'Owned',
        rating: null,
        notes: null,
        coverUpdate: { intent: 'keep' },
      },
    ]);
  });

  it('orders shared form fields before board-game play details', () => {
    configure();

    const root = fixture.nativeElement as HTMLElement;
    const orderedIds = [
      'board-game-form-name',
      'board-game-form-cover',
      'board-game-form-acquisition',
      'board-game-form-family-friendly',
      'board-game-form-minimum',
      'board-game-form-duration',
      'board-game-form-rating',
      'board-game-form-notes',
    ];
    const positions = orderedIds.map((id) => {
      const element = id === 'board-game-form-cover' ? root.querySelector('app-cover-image-search input[type="url"]') : root.querySelector(`#${id}`);
      if (element === null) {
        throw new Error(`Missing #${id}`);
      }
      return [...root.querySelectorAll('input, select, textarea')].indexOf(element);
    });

    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });

  it('renders cover search in add mode without searching automatically', () => {
    configure();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Search cover');
  });

  it('renders cover search in edit mode without searching automatically', () => {
    configure(ownedGame);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Search cover');
  });

  it('does not require changing the default InteractionType or setting duration to save', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '1');
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].interactionType).toBe('Competitive');
    expect(emitted[0].approximateDuration).toBeNull();
  });

  it('submits an explicit interaction type and duration', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');
    setNumber('#board-game-form-duration', '60');
    selectOption('#board-game-form-interaction', 'Competitive');
    submit();

    expect(emitted[0].approximateDuration).toBe(60);
    expect(emitted[0].interactionType).toBe('Competitive');
  });

  it('maps the None interaction option to null', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');
    selectOption('#board-game-form-interaction', 'Cooperative');
    selectOption('#board-game-form-interaction', 'None');
    submit();

    expect(emitted[0].interactionType).toBeNull();
  });

  it('requires a name', () => {
    configure();
    const emitted = collectSubmitted();

    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Board game name is required.');
    expect(emitted).toEqual([]);
  });

  it('rejects a whitespace-only name', () => {
    configure();
    const emitted = collectSubmitted();

    setName('   ');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Board game name is required.');
    expect(emitted).toEqual([]);
  });

  it('trims a name whose raw length exceeds 100 and accepts it', () => {
    configure();
    const emitted = collectSubmitted();

    const padding = ' '.repeat(3);
    setName(`${padding}${'C'.repeat(99)}${padding}`);
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '1');
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].name).toBe('C'.repeat(99));
  });

  it('rejects a trimmed name longer than 100 characters', () => {
    configure();
    const emitted = collectSubmitted();

    setName('C'.repeat(101));
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('at most 100 characters');
    expect(emitted).toEqual([]);
  });

  it('requires minimum and maximum players', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Minimum players must be at least 1.');
    expect(emitted).toEqual([]);
  });

  it('rejects a minimum below 1', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '0');
    setNumber('#board-game-form-maximum', '4');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Minimum players must be at least 1.');
    expect(emitted).toEqual([]);
  });

  it('rejects a maximum below the minimum', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '4');
    setNumber('#board-game-form-maximum', '2');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain(
      'Maximum players must be at least the minimum players.',
    );
    expect(emitted).toEqual([]);
  });

  it('rejects a non-positive duration', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '2');
    setNumber('#board-game-form-duration', '0');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Duration must be a positive number of minutes.');
    expect(emitted).toEqual([]);
  });

  it('accepts equal minimum and maximum players', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Solo');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '1');
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].minimumPlayers).toBe(1);
    expect(emitted[0].maximumPlayers).toBe(1);
  });

  it('rejects a rating outside the 1-5 range', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');
    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    form.controls['rating'].setValue(6);

    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Rating must be between 1 and 5.');
    expect(emitted).toEqual([]);
  });

  it('trims notes and the cover URL before sending them', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');

    const notesInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-notes') as HTMLTextAreaElement;
    notesInput.value = '  fun  ';
    notesInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('app-cover-image-search input[type="url"]') as HTMLInputElement;
    coverInput.value = '  https://example.com/img.png  ';
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submit();

    expect(emitted[0].notes).toBe('fun');
    expect(emitted[0].coverUpdate).toEqual({ intent: 'external', url: 'https://example.com/img.png' });
  });

  it('rejects notes longer than 5000 characters after trimming', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');

    const notesInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-notes') as HTMLTextAreaElement;
    notesInput.value = 'n'.repeat(5001);
    notesInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Notes must be at most 5000 characters.');
    expect(emitted).toEqual([]);
  });

  it('rejects a cover URL that is not http(s)', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('app-cover-image-search input[type="url"]') as HTMLInputElement;
    coverInput.value = 'not-a-url';
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cover image URL is invalid.');
    expect(emitted).toEqual([]);
  });

  it('loads the existing game values when editing', () => {
    configure(ownedGame);
    const emitted = collectSubmitted();

    const nameInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Catan');

    const minimum = (fixture.nativeElement as HTMLElement).querySelector(
      '#board-game-form-minimum',
    ) as HTMLInputElement;
    expect(minimum.value).toBe('3');
    const maximum = (fixture.nativeElement as HTMLElement).querySelector(
      '#board-game-form-maximum',
    ) as HTMLInputElement;
    expect(maximum.value).toBe('4');

    setName('  Catan II  ');
    submit();

    expect(emitted[0].name).toBe('Catan II');
    expect(emitted[0].minimumPlayers).toBe(3);
    expect(emitted[0].maximumPlayers).toBe(4);
    expect(emitted[0].approximateDuration).toBe(60);
    expect(emitted[0].interactionType).toBe('Cooperative');
    expect(emitted[0].acquisitionStatus).toBe('Owned');
    expect(emitted[0].rating).toBe(5);
    expect(emitted[0].notes).toBe('Trading');
  });

  it('submits true when Family friendly is selected on Add', () => {
    configure();
    const emitted = collectSubmitted();
    setName('Family game');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '4');
    const checkbox = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-family-friendly') as HTMLInputElement;
    checkbox.click();
    fixture.detectChanges();
    submit();

    expect(emitted[0].isFamilyFriendly).toBe(true);
  });

  it.each([false, true])('initializes Family friendly to %s on Edit and saves its opposite', (value) => {
    configure({ ...ownedGame, isFamilyFriendly: value });
    const emitted = collectSubmitted();
    const checkbox = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-family-friendly') as HTMLInputElement;
    expect(checkbox.checked).toBe(value);
    submit();
    expect(emitted[0].isFamilyFriendly).toBe(value);

    checkbox.click();
    fixture.detectChanges();
    submit();

    expect(emitted[1]).toEqual({ ...emitted[0], isFamilyFriendly: !value });
  });

  it('submits Wishlist without clearing any field', () => {
    configure();
    const emitted = collectSubmitted();

    selectOption('#board-game-form-acquisition', 'Wishlist');
    setName('Catan');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '1');
    submit();

    expect(emitted[0].acquisitionStatus).toBe('Wishlist');
  });

  it('keeps a saved managed cover on metadata-only submit, and explicitly removes it with the full snapshot', () => {
    configure({ ...ownedGame, coverUploadId: 'managed' });
    const emitted = collectSubmitted();
    setName('Other edits');
    submit();
    expect(emitted[0].coverUpdate).toEqual({ intent: 'keep' });
    expect(emitted[0]).not.toHaveProperty('coverImageUrl');
    const selector = fixture.debugElement.query(By.directive(CoverImageSearch)).componentInstance as CoverImageSearch;
    expect(selector.savedCover()?.coverUploadId).toBe('managed');
    selector.draftChanged.emit({ intent: 'remove' });
    submit();
    expect(emitted[1].coverUpdate).toEqual({ intent: 'remove', expectedCover: { coverImageUrl: null, coverUploadId: 'managed' } });
  });

  it('a refreshed cover snapshot does not reinitialize metadata or infer keep from equal external values', () => {
    configure({ ...ownedGame, coverImageUrl: 'https://example.test/a.jpg' });
    const emitted = collectSubmitted();
    setName('Unsaved edits');
    fixture.componentInstance.draft.set({ intent: 'external', url: 'https://example.test/a.jpg' });
    fixture.componentRef.setInput('savedCover', { coverImageUrl: null, coverUploadId: 'newer' });
    fixture.detectChanges();
    submit();
    expect(emitted[0].name).toBe('Unsaved edits');
    expect(emitted[0].coverUpdate).toEqual({
      intent: 'external', url: 'https://example.test/a.jpg', expectedCover: { coverImageUrl: null, coverUploadId: 'newer' },
    });
  });

  it('blocks submit while the selector is preparing/editor-open or the parent is saving', () => {
    configure(ownedGame);
    const emitted = collectSubmitted();
    const selector = fixture.debugElement.query(By.directive(CoverImageSearch)).componentInstance as CoverImageSearch;
    selector.busyChanged.emit(true);
    fixture.componentInstance.submit();
    expect(emitted).toEqual([]);
    selector.busyChanged.emit(false);
    fixture.componentRef.setInput('disabled', true);
    fixture.componentInstance.submit();
    expect(emitted).toEqual([]);
    fixture.componentRef.setInput('disabled', false);
    fixture.componentInstance.submit();
    expect(emitted).toHaveLength(1);
  });
});
