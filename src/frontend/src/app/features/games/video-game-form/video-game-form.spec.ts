import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';
import { provideRouter } from '@angular/router';

import { Platform } from '../../platforms/platform';

import { Genre } from '../genres';
import { VideoGame, VideoGameInput } from '../video-game';

import { VideoGameForm } from './video-game-form';

const steam: Platform = { id: 'p1', name: 'Steam' };
const xbox: Platform = { id: 'p2', name: 'Xbox' };
const action: Genre = { id: 'g1', name: 'Action' };

const ownedGame: VideoGame = {
  id: '1',
  name: 'Hades',
  coverImageUrl: null,
  acquisitionStatus: 'Owned',
  platformIds: ['p1'],
  genreIds: ['g1'],
  gameStatus: 'Playing',
  progressPercentage: 50,
  rating: 5,
  notes: 'Roguelike',
  createdAt: '2026-08-17T00:00:00Z',
  minimumPlayers: 1,
  maximumPlayers: 2,
};

describe('VideoGameForm', () => {
  let fixture: ComponentFixture<VideoGameForm>;
  let httpMock: HttpTestingController;

  function configure(game: VideoGame | null = null): void {
    TestBed.configureTestingModule({
      imports: [VideoGameForm],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    fixture = TestBed.createComponent(VideoGameForm);
    fixture.componentRef.setInput('game', game);
    fixture.componentRef.setInput('submitLabel', 'Save');
    fixture.detectChanges();
    httpMock = TestBed.inject(HttpTestingController);
  }

  function flushOptions(platforms: Platform[] = [], genres: Genre[] = [action]): void {
    const platformReq = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/platforms'));
    platformReq.flush(platforms);
    const genreReq = httpMock.expectOne((request) => request.method === 'GET' && request.url.endsWith('/genres'));
    genreReq.flush(genres);
    fixture.detectChanges();
  }

  function collectSubmitted(): VideoGameInput[] {
    const emitted: VideoGameInput[] = [];
    fixture.componentInstance.submitted.subscribe((value) => emitted.push(value));
    return emitted;
  }

  function setName(value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector('#game-form-name') as HTMLInputElement;
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

  function setProgress(value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector('#game-form-progress') as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function setNumberInput(selector: string, value: string): void {
    const input = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLInputElement;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('defaults to Owned and preselects the single Platform for a quick add', () => {
    configure();
    flushOptions([steam], [action]);

    const platformCheckbox = (fixture.nativeElement as HTMLElement).querySelector('.game-form__platform') as HTMLInputElement;
    expect(platformCheckbox.checked).toBe(true);

    const emitted = collectSubmitted();
    setName('  Hades  ');
    submit();

    expect(emitted).toEqual([
      {
        name: 'Hades',
        coverImageUrl: null,
        acquisitionStatus: 'Owned',
        platformIds: ['p1'],
        genreIds: [],
        gameStatus: null,
        progressPercentage: null,
        rating: null,
        notes: null,
        minimumPlayers: null,
        maximumPlayers: null,
      },
    ]);
  });

  it('orders shared form fields before game-specific details', () => {
    configure();
    flushOptions([steam], [action]);

    const root = fixture.nativeElement as HTMLElement;
    const orderedIds = [
      'game-form-name',
      'game-form-cover',
      'game-form-acquisition',
      'game-form-status',
      'game-form-minimum-players',
      'game-form-rating',
      'game-form-notes',
    ];
    const positions = orderedIds.map((id) => {
      const element = root.querySelector(`#${id}`);
      if (element === null) {
        throw new Error(`Missing #${id}`);
      }
      return [...root.querySelectorAll('input, select, textarea')].indexOf(element);
    });

    expect(positions).toEqual([...positions].sort((a, b) => a - b));
  });

  it('renders platform and genre choices as checkbox cards', () => {
    configure();
    flushOptions([steam], [action]);

    expect((fixture.nativeElement as HTMLElement).querySelectorAll('.game-form__check-card').length).toBe(2);
  });

  it('renders cover search in add mode without searching automatically', () => {
    configure();
    flushOptions([steam], [action]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Search cover');
  });

  it('renders cover search in edit mode without searching automatically', () => {
    configure(ownedGame);
    flushOptions([steam], [action]);

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Search cover');
  });

  it('does not preselect a Platform when the user has more than one', () => {
    configure();
    flushOptions([steam, xbox], [action]);

    const checkboxes = [...(fixture.nativeElement as HTMLElement).querySelectorAll('.game-form__platform')] as HTMLInputElement[];
    expect(checkboxes.every((checkbox) => !checkbox.checked)).toBe(true);
  });

  it('shows guidance and blocks submit when Owned with no Platforms', () => {
    configure();
    flushOptions([], [action]);
    const emitted = collectSubmitted();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('You have no platforms yet');

    setName('Hades');
    submit();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('An Owned game requires at least one platform. Add one in the Platforms page first.');
    expect(emitted).toEqual([]);
  });

  it('blocks submit when Owned with Platforms available but none selected', () => {
    configure();
    flushOptions([steam, xbox], [action]);
    const emitted = collectSubmitted();

    setName('Hades');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('An Owned game requires at least one platform.');
    expect(emitted).toEqual([]);
  });

  it('submits Wishlist with zero Platforms and null status and progress', () => {
    configure();
    flushOptions([], [action]);
    const emitted = collectSubmitted();

    selectOption('#game-form-acquisition', 'Wishlist');
    setName('Stardew Valley');
    submit();

    expect(emitted).toEqual([
      {
        name: 'Stardew Valley',
        coverImageUrl: null,
        acquisitionStatus: 'Wishlist',
        platformIds: [],
        genreIds: [],
        gameStatus: null,
        progressPercentage: null,
        rating: null,
        notes: null,
        minimumPlayers: null,
        maximumPlayers: null,
      },
    ]);
  });

  it('clears game status and progress and hides their controls when not Owned', () => {
    configure();
    flushOptions([steam], [action]);

    selectOption('#game-form-status', 'Playing');
    setProgress('50');
    selectOption('#game-form-acquisition', 'Wishlist');

    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-status')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-progress')).toBeNull();

    const emitted = collectSubmitted();
    setName('Stardew Valley');
    submit();

    expect(emitted[0].gameStatus).toBeNull();
    expect(emitted[0].progressPercentage).toBeNull();
  });

  it('re-enables status and progress when switching back to Owned', () => {
    configure();
    flushOptions([steam], [action]);

    selectOption('#game-form-acquisition', 'Wishlist');
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-status')).toBeNull();

    selectOption('#game-form-acquisition', 'Owned');
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-status')).not.toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-progress')).not.toBeNull();
  });

  it('loads the existing game values when editing', () => {
    configure(ownedGame);
    flushOptions([steam], [action]);

    const nameInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-name') as HTMLInputElement;
    expect(nameInput.value).toBe('Hades');
    expect((fixture.nativeElement as HTMLElement).querySelector('#game-form-status')).not.toBeNull();

    const emitted = collectSubmitted();
    setName('  Hades II  ');
    submit();

    expect(emitted[0].name).toBe('Hades II');
    expect(emitted[0].acquisitionStatus).toBe('Owned');
    expect(emitted[0].platformIds).toEqual(['p1']);
    expect(emitted[0].genreIds).toEqual(['g1']);
    expect(emitted[0].gameStatus).toBe('Playing');
    expect(emitted[0].progressPercentage).toBe(50);
    expect(emitted[0].rating).toBe(5);
    expect(emitted[0].notes).toBe('Roguelike');
    expect(emitted[0].minimumPlayers).toBe(1);
    expect(emitted[0].maximumPlayers).toBe(2);
  });

  it('submits optional player counts when both are provided', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('Hades');
    setNumberInput('#game-form-minimum-players', '1');
    setNumberInput('#game-form-maximum-players', '2');
    submit();

    expect(emitted[0].minimumPlayers).toBe(1);
    expect(emitted[0].maximumPlayers).toBe(2);
  });

  it('blocks submit when only one player count is provided', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('Hades');
    setNumberInput('#game-form-minimum-players', '1');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Minimum and maximum players must both be provided.');
    expect(emitted).toEqual([]);
  });

  it('blocks submit when maximum players is less than minimum players', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('Hades');
    setNumberInput('#game-form-minimum-players', '3');
    setNumberInput('#game-form-maximum-players', '2');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Player counts must be provided together');
    expect(emitted).toEqual([]);
  });

  it('sets progress to 100 when game status is Completed', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('Hades');
    selectOption('#game-form-status', 'Completed');
    submit();

    expect(emitted[0].gameStatus).toBe('Completed');
    expect(emitted[0].progressPercentage).toBe(100);
  });

  it('requires a name', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Video game name is required.');
    expect(emitted).toEqual([]);
  });

  it('rejects a whitespace-only name', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('   ');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Video game name is required.');
    expect(emitted).toEqual([]);
  });

  it('trims a name whose raw length exceeds 100 and accepts it', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const padding = ' '.repeat(3);
    setName(`${padding}${'H'.repeat(99)}${padding}`);
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].name).toBe('H'.repeat(99));
  });

  it('rejects a trimmed name longer than 100 characters', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setName('H'.repeat(101));
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('at most 100 characters');
    expect(emitted).toEqual([]);
  });

  it('trims notes and the cover URL before sending them', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const notesInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-notes') as HTMLTextAreaElement;
    notesInput.value = '  fun  ';
    notesInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-cover') as HTMLInputElement;
    coverInput.value = '  https://example.com/img.png  ';
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    setName('Hades');
    submit();

    expect(emitted[0].notes).toBe('fun');
    expect(emitted[0].coverImageUrl).toBe('https://example.com/img.png');
  });

  it('accepts a cover URL whose raw length exceeds 2048 but trimmed value is within limit', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const base = 'https://example.com/' + 'a'.repeat(2048 - 'https://example.com/'.length);
    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-cover') as HTMLInputElement;
    coverInput.value = ` ${base} `;
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    setName('Hades');
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].coverImageUrl).toBe(base);
  });

  it('rejects a cover URL that is not http(s)', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-cover') as HTMLInputElement;
    coverInput.value = 'not-a-url';
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    setName('Hades');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Cover image URL is invalid.');
    expect(emitted).toEqual([]);
  });

  it('rejects notes longer than 5000 characters after trimming', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const notesInput = (fixture.nativeElement as HTMLElement).querySelector('#game-form-notes') as HTMLTextAreaElement;
    notesInput.value = 'n'.repeat(5001);
    notesInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    setName('Hades');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Notes must be at most 5000 characters.');
    expect(emitted).toEqual([]);
  });

  it('rejects progress outside the 0-100 range', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    setProgress('150');
    setName('Hades');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Progress must be between 0 and 100.');
    expect(emitted).toEqual([]);
  });

  it('rejects a rating outside the 1-5 range', () => {
    configure();
    flushOptions([steam], [action]);
    const emitted = collectSubmitted();

    const form = (fixture.componentInstance as unknown as { form: FormGroup }).form;
    form.controls['rating'].setValue(6);

    setName('Hades');
    submit();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Rating must be between 1 and 5.');
    expect(emitted).toEqual([]);
  });
});
