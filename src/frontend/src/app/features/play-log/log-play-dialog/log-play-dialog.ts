import { Component, ElementRef, output, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import { CreatePlayLogEntryRequest, PlayLogEntry, UpdatePlayLogEntryRequest } from '../play-log';

export interface LogPlayDialogGame {
  id: string;
  name: string;
}

export interface UpdatePlayLogEntryDialogRequest {
  id: string;
  request: UpdatePlayLogEntryRequest;
}

type LogPlayDialogMode = 'create' | 'edit';

@Component({
  selector: 'app-log-play-dialog',
  imports: [ReactiveFormsModule],
  templateUrl: './log-play-dialog.html',
  styleUrl: './log-play-dialog.scss',
})
export class LogPlayDialog {
  private readonly dialog = viewChild<ElementRef<HTMLDialogElement>>('dialog');

  readonly createSubmitted = output<CreatePlayLogEntryRequest>();
  readonly updateSubmitted = output<UpdatePlayLogEntryDialogRequest>();
  readonly cancelled = output<void>();

  protected readonly mode = signal<LogPlayDialogMode>('create');
  protected readonly gameName = signal('');
  private readonly gameId = signal<string | null>(null);
  private readonly entryId = signal<string | null>(null);
  protected readonly validationError = signal('');
  protected readonly serverError = signal('');
  protected readonly pending = signal(false);

  protected readonly form = new FormGroup({
    playedAt: new FormControl('', { nonNullable: true }),
    durationMinutes: new FormControl<number | null>(null),
  });

  open(game: LogPlayDialogGame): void {
    this.mode.set('create');
    this.gameId.set(game.id);
    this.entryId.set(null);
    this.gameName.set(game.name);
    this.resetState(toDateTimeLocalValue(new Date()), null);
    this.openDialog();
  }

  openEdit(entry: PlayLogEntry): void {
    this.mode.set('edit');
    this.gameId.set(null);
    this.entryId.set(entry.id);
    this.gameName.set(entry.gameName);
    this.resetState(toDateTimeLocalValue(new Date(entry.playedAt)), entry.durationMinutes);
    this.openDialog();
  }

  setPending(pending: boolean): void {
    this.pending.set(pending);
  }

  showError(message: string): void {
    this.serverError.set(message);
    this.pending.set(false);
  }

  close(): void {
    this.closeDialog();
  }

  protected submit(): void {
    if (this.pending()) {
      return;
    }

    const playedAt = this.toPlayedAtIso();
    if (playedAt === null) {
      return;
    }

    const durationMinutes = this.toDurationMinutes();
    if (durationMinutes === undefined) {
      return;
    }

    this.validationError.set('');
    this.serverError.set('');
    if (this.mode() === 'create') {
      const gameId = this.gameId();
      if (gameId !== null) {
        this.createSubmitted.emit({ gameId, playedAt, durationMinutes });
      }
      return;
    }

    const id = this.entryId();
    if (id !== null) {
      this.updateSubmitted.emit({ id, request: { playedAt, durationMinutes } });
    }
  }

  protected cancel(): void {
    if (this.pending()) {
      return;
    }

    this.closeDialog();
    this.cancelled.emit();
  }

  protected onDialogCancel(event: Event): void {
    event.preventDefault();
    this.cancel();
  }

  private toPlayedAtIso(): string | null {
    const playedAtValue = this.form.controls.playedAt.value;
    if (playedAtValue.trim().length === 0) {
      this.form.controls.playedAt.markAsTouched();
      this.form.controls.playedAt.setErrors({ required: true });
      this.validationError.set('Choose when you played.');
      return null;
    }

    const playedAt = new Date(playedAtValue);
    if (Number.isNaN(playedAt.getTime())) {
      this.form.controls.playedAt.markAsTouched();
      this.form.controls.playedAt.setErrors({ invalid: true });
      this.validationError.set('Choose a valid played date and time.');
      return null;
    }

    this.form.controls.playedAt.setErrors(null);
    return playedAt.toISOString();
  }

  private toDurationMinutes(): number | null | undefined {
    const duration = this.form.controls.durationMinutes.value;
    if (duration === null) {
      this.form.controls.durationMinutes.setErrors(null);
      return null;
    }

    if (Number.isNaN(duration) || !Number.isInteger(duration) || duration < 1) {
      this.form.controls.durationMinutes.markAsTouched();
      this.form.controls.durationMinutes.setErrors({ invalid: true });
      this.validationError.set('Duration must be a whole number greater than 0.');
      return undefined;
    }

    this.form.controls.durationMinutes.setErrors(null);
    return duration;
  }

  private resetState(playedAt: string, durationMinutes: number | null): void {
    this.validationError.set('');
    this.serverError.set('');
    this.pending.set(false);
    this.form.reset({ playedAt, durationMinutes });
    this.form.controls.playedAt.setErrors(null);
    this.form.controls.durationMinutes.setErrors(null);
  }

  private openDialog(): void {
    const dialog = this.dialog()?.nativeElement;
    if (dialog && !dialog.open) {
      if (typeof dialog.showModal === 'function') {
        dialog.showModal();
      } else {
        dialog.setAttribute('open', '');
      }
    }
  }

  private closeDialog(): void {
    const dialog = this.dialog()?.nativeElement;
    if (dialog?.open) {
      if (typeof dialog.close === 'function') {
        dialog.close();
      } else {
        dialog.removeAttribute('open');
      }
    }
    this.gameName.set('');
    this.gameId.set(null);
    this.entryId.set(null);
    this.validationError.set('');
    this.serverError.set('');
    this.pending.set(false);
  }
}

function toDateTimeLocalValue(date: Date): string {
  const year = date.getFullYear();
  const month = pad(date.getMonth() + 1);
  const day = pad(date.getDate());
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function pad(value: number): string {
  return value.toString().padStart(2, '0');
}
