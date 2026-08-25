import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CreatePlayLogEntryRequest } from '../play-log';

import { LogPlayDialog, UpdatePlayLogEntryDialogRequest } from './log-play-dialog';

type DialogForm = LogPlayDialog['form'];

function text(fixture: ComponentFixture<LogPlayDialog>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

function setInput(fixture: ComponentFixture<LogPlayDialog>, selector: string, value: string): void {
  const input = (fixture.nativeElement as HTMLElement).querySelector(selector) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  fixture.detectChanges();
}

describe('LogPlayDialog', () => {
  let fixture: ComponentFixture<LogPlayDialog>;
  let component: LogPlayDialog;
  let createSubmitted: CreatePlayLogEntryRequest | undefined;
  let updateSubmitted: UpdatePlayLogEntryDialogRequest | undefined;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [LogPlayDialog] });
    fixture = TestBed.createComponent(LogPlayDialog);
    component = fixture.componentInstance;
    createSubmitted = undefined;
    updateSubmitted = undefined;
    component.createSubmitted.subscribe((request) => (createSubmitted = request));
    component.updateSubmitted.subscribe((request) => (updateSubmitted = request));
    fixture.detectChanges();
  });

  it('renders no form until opened', () => {
    expect(text(fixture)).not.toContain('Played date/time');
  });

  it('emits a reviewed play request with local played time converted to ISO', () => {
    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Hades');
    expect(((fixture.nativeElement as HTMLElement).querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toMatch(
      /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/,
    );
    expect(((fixture.nativeElement as HTMLElement).querySelector('input[type="number"]') as HTMLInputElement).value).toBe('');
    setInput(fixture, 'input[type="datetime-local"]', '2026-08-24T18:30');
    setInput(fixture, 'input[type="number"]', '90');
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(createSubmitted).toEqual({
      gameId: 'game-1',
      playedAt: new Date('2026-08-24T18:30').toISOString(),
      durationMinutes: 90,
    });
    expect(text(fixture)).toContain('Hades');
  });

  it('cancels without emitting a create request', () => {
    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();

    [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')]
      .find((button) => button.textContent?.trim() === 'Cancel')
      ?.dispatchEvent(new MouseEvent('click'));
    fixture.detectChanges();

    expect(createSubmitted).toBeUndefined();
    expect(text(fixture)).not.toContain('Hades');
  });

  it('allows an omitted duration', () => {
    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();

    setInput(fixture, 'input[type="datetime-local"]', '2026-08-24T18:30');
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();

    expect(createSubmitted?.durationMinutes).toBeNull();
  });

  it('opens edit mode prepopulated and emits only editable fields', () => {
    component.openEdit({
      id: 'log-1',
      libraryEntryId: 'entry-1',
      gameId: 'game-1',
      gameType: 'VideoGame',
      gameName: 'Hades',
      coverImageUrl: null,
      playedAt: '2026-08-24T18:30:00Z',
      durationMinutes: 90,
      createdAt: '2026-08-24T19:00:00Z',
    });
    fixture.detectChanges();

    expect(text(fixture)).toContain('Edit play log');
    expect(text(fixture)).toContain('The game cannot be changed');
    expect(((fixture.nativeElement as HTMLElement).querySelector('input[type="number"]') as HTMLInputElement).value).toBe('90');

    setInput(fixture, 'input[type="datetime-local"]', '2026-08-25T12:00');
    setInput(fixture, 'input[type="number"]', '120');
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(updateSubmitted).toEqual({
      id: 'log-1',
      request: {
        playedAt: new Date('2026-08-25T12:00').toISOString(),
        durationMinutes: 120,
      },
    });
  });

  it('validates required played time and positive whole-minute duration', () => {
    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();

    (component as unknown as { form: DialogForm }).form.controls.playedAt.setValue('');
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(text(fixture)).toContain('Choose when you played.');
    expect(createSubmitted).toBeUndefined();

    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();
    const form = (component as unknown as { form: DialogForm }).form;
    form.controls.playedAt.setValue('2026-08-24T18:30');
    form.controls.durationMinutes.setValue(0);
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(text(fixture)).toContain('Duration must be a whole number greater than 0.');
    expect(createSubmitted).toBeUndefined();
  });

  it('shows pending and server errors without clearing entered values', () => {
    component.open({ id: 'game-1', name: 'Hades' });
    fixture.detectChanges();
    setInput(fixture, 'input[type="datetime-local"]', '2026-08-24T18:30');
    setInput(fixture, 'input[type="number"]', '90');

    component.setPending(true);
    fixture.detectChanges();
    expect(((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(true);

    component.showError('Unable to log this play.');
    fixture.detectChanges();
    expect(text(fixture)).toContain('Unable to log this play.');
    expect(((fixture.nativeElement as HTMLElement).querySelector('input[type="datetime-local"]') as HTMLInputElement).value).toBe(
      '2026-08-24T18:30',
    );
    expect(((fixture.nativeElement as HTMLElement).querySelector('input[type="number"]') as HTMLInputElement).value).toBe('90');
  });
});
