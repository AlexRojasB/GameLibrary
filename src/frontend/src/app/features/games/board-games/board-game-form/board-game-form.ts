import { Component, effect, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';

import type { AcquisitionStatus } from '../../acquisition-status';
import { BoardGame, BoardGameInput, InteractionType } from '../../board-game';

const ACQUISITION_STATUSES: AcquisitionStatus[] = ['Owned', 'Wishlist', 'Interested'];
const INTERACTION_TYPES: InteractionType[] = ['Cooperative', 'Competitive'];

@Component({
  selector: 'app-board-game-form',
  imports: [ReactiveFormsModule],
  templateUrl: './board-game-form.html',
  styleUrl: './board-game-form.scss',
})
export class BoardGameForm {
  readonly game = input<BoardGame | null>(null);
  readonly submitLabel = input('Save');
  readonly serverError = input('');

  readonly submitted = output<BoardGameInput>();
  readonly cancelled = output<void>();

  protected readonly acquisitionStatuses = ACQUISITION_STATUSES;
  protected readonly interactionTypes = INTERACTION_TYPES;
  protected readonly validationError = signal('');

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    minimumPlayers: new FormControl<number | null>(null),
    maximumPlayers: new FormControl<number | null>(null),
    approximateDuration: new FormControl<number | null>(null),
    interactionType: new FormControl<InteractionType | null>(null),
    acquisitionStatus: new FormControl<AcquisitionStatus>('Owned', { nonNullable: true }),
    rating: new FormControl<number | null>(null),
    notes: new FormControl<string | null>(null),
    coverImageUrl: new FormControl<string | null>(null),
  });

  constructor() {
    effect(() => {
      const game = this.game();
      if (game === null) {
        return;
      }
      this.form.controls.name.setValue(game.name, { emitEvent: false });
      this.form.controls.minimumPlayers.setValue(game.minimumPlayers, { emitEvent: false });
      this.form.controls.maximumPlayers.setValue(game.maximumPlayers, { emitEvent: false });
      this.form.controls.approximateDuration.setValue(game.approximateDuration, { emitEvent: false });
      this.form.controls.interactionType.setValue(game.interactionType, { emitEvent: false });
      this.form.controls.acquisitionStatus.setValue(game.acquisitionStatus);
      this.form.controls.rating.setValue(game.rating, { emitEvent: false });
      this.form.controls.notes.setValue(game.notes, { emitEvent: false });
      this.form.controls.coverImageUrl.setValue(game.coverImageUrl, { emitEvent: false });
    });
  }

  submit(): void {
    const validationError = this.validate();
    if (validationError !== null) {
      if (validationError !== '') {
        this.validationError.set(validationError);
      }
      return;
    }
    this.validationError.set('');

    const f = this.form.controls;
    this.submitted.emit({
      name: f.name.value.trim(),
      minimumPlayers: f.minimumPlayers.value,
      maximumPlayers: f.maximumPlayers.value,
      approximateDuration: f.approximateDuration.value,
      interactionType: f.interactionType.value,
      acquisitionStatus: f.acquisitionStatus.value,
      rating: f.rating.value,
      notes: trimToNull(f.notes.value),
      coverImageUrl: trimToNull(f.coverImageUrl.value),
    });
  }

  private validate(): string | null {
    const f = this.form.controls;

    const name = f.name.value.trim();
    if (name.length === 0) {
      f.name.markAsTouched();
      f.name.setErrors({ required: true });
      return '';
    }
    if (name.length > 100) {
      f.name.markAsTouched();
      f.name.setErrors({ maxlength: { requiredLength: 100, actualLength: name.length } });
      return '';
    }
    f.name.setErrors(null);

    const minimumPlayers = f.minimumPlayers.value;
    if (minimumPlayers === null || Number.isNaN(minimumPlayers) || !Number.isInteger(minimumPlayers) || minimumPlayers < 1) {
      f.minimumPlayers.markAsTouched();
      f.minimumPlayers.setErrors({ required: true });
      return '';
    }
    f.minimumPlayers.setErrors(null);

    const maximumPlayers = f.maximumPlayers.value;
    if (
      maximumPlayers === null ||
      Number.isNaN(maximumPlayers) ||
      !Number.isInteger(maximumPlayers) ||
      maximumPlayers < minimumPlayers
    ) {
      f.maximumPlayers.markAsTouched();
      f.maximumPlayers.setErrors({ required: true });
      return '';
    }
    f.maximumPlayers.setErrors(null);

    const duration = f.approximateDuration.value;
    if (duration !== null && (Number.isNaN(duration) || !Number.isInteger(duration) || duration < 1)) {
      f.approximateDuration.markAsTouched();
      f.approximateDuration.setErrors({ invalid: true });
      return '';
    }
    f.approximateDuration.setErrors(null);

    const notes = trimToNull(f.notes.value);
    if (notes !== null && notes.length > 5000) {
      f.notes.markAsTouched();
      f.notes.setErrors({ maxlength: { requiredLength: 5000, actualLength: notes.length } });
      return '';
    }
    f.notes.setErrors(null);

    const cover = trimToNull(f.coverImageUrl.value);
    if (cover !== null && (cover.length > 2048 || !/^https?:\/\//i.test(cover))) {
      f.coverImageUrl.markAsTouched();
      f.coverImageUrl.setErrors({ invalid: true });
      return '';
    }
    f.coverImageUrl.setErrors(null);

    const rating = f.rating.value;
    if (rating !== null && (Number.isNaN(rating) || rating < 1 || rating > 5)) {
      f.rating.markAsTouched();
      f.rating.setErrors({ range: true });
      return '';
    }
    f.rating.setErrors(null);

    return null;
  }
}

function trimToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length === 0 ? null : trimmed;
}