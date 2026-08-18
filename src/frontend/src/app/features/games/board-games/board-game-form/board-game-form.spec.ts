import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormGroup } from '@angular/forms';

import { BoardGame, BoardGameInput } from '../../board-game';

import { BoardGameForm } from './board-game-form';

const ownedGame: BoardGame = {
  id: '1',
  name: 'Catan',
  coverImageUrl: null,
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

  function configure(game: BoardGame | null = null): void {
    TestBed.configureTestingModule({
      imports: [BoardGameForm],
    });
    fixture = TestBed.createComponent(BoardGameForm);
    fixture.componentRef.setInput('game', game);
    fixture.componentRef.setInput('submitLabel', 'Save');
    fixture.detectChanges();
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

  it('quick-adds an Owned board game with name and player counts only', () => {
    configure();
    const emitted = collectSubmitted();

    setName('  Catan  ');
    setNumber('#board-game-form-minimum', '3');
    setNumber('#board-game-form-maximum', '4');
    submit();

    expect(emitted).toEqual([
      {
        name: 'Catan',
        minimumPlayers: 3,
        maximumPlayers: 4,
        approximateDuration: null,
        interactionType: null,
        acquisitionStatus: 'Owned',
        rating: null,
        notes: null,
        coverImageUrl: null,
      },
    ]);
  });

  it('does not require an InteractionType or duration to save', () => {
    configure();
    const emitted = collectSubmitted();

    setName('Catan');
    setNumber('#board-game-form-minimum', '1');
    setNumber('#board-game-form-maximum', '1');
    submit();

    expect(emitted.length).toBe(1);
    expect(emitted[0].interactionType).toBeNull();
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

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-cover') as HTMLInputElement;
    coverInput.value = '  https://example.com/img.png  ';
    coverInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    submit();

    expect(emitted[0].notes).toBe('fun');
    expect(emitted[0].coverImageUrl).toBe('https://example.com/img.png');
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

    const coverInput = (fixture.nativeElement as HTMLElement).querySelector('#board-game-form-cover') as HTMLInputElement;
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
});