import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';

import { Genre } from '../genres';
import { GenresService } from '../genres.service';
import { AcquisitionStatus, GameStatus, VideoGame, VideoGameInput } from '../video-game';

const ACQUISITION_STATUSES: AcquisitionStatus[] = ['Owned', 'Wishlist', 'Interested'];
const GAME_STATUSES: GameStatus[] = ['Backlog', 'Playing', 'Completed', 'Abandoned', 'WantToPlay'];

@Component({
  selector: 'app-video-game-form',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './video-game-form.html',
  styleUrl: './video-game-form.scss',
})
export class VideoGameForm {
  readonly game = input<VideoGame | null>(null);
  readonly submitLabel = input('Save');
  readonly serverError = input('');

  readonly submitted = output<VideoGameInput>();
  readonly cancelled = output<void>();

  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);

  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly loadError = signal(false);
  protected readonly validationError = signal('');

  protected readonly acquisitionStatuses = ACQUISITION_STATUSES;
  protected readonly gameStatuses = GAME_STATUSES;
  protected readonly acquisitionStatus = signal<AcquisitionStatus>('Owned');
  protected readonly isOwned = computed(() => this.acquisitionStatus() === 'Owned');

  protected readonly form = new FormGroup({
    name: new FormControl('', { nonNullable: true }),
    coverImageUrl: new FormControl<string | null>(null),
    acquisitionStatus: new FormControl<AcquisitionStatus>('Owned', { nonNullable: true }),
    platformIds: new FormControl<string[]>([], { nonNullable: true }),
    genreIds: new FormControl<string[]>([], { nonNullable: true }),
    gameStatus: new FormControl<GameStatus | null>(null),
    progressPercentage: new FormControl<number | null>(null),
    rating: new FormControl<number | null>(null),
    notes: new FormControl<string | null>(null),
    minimumPlayers: new FormControl<number | null>(null),
    maximumPlayers: new FormControl<number | null>(null),
  });

  constructor() {
    this.loadPlatforms();
    this.loadGenres();

    this.form.controls.acquisitionStatus.valueChanges.subscribe((value) => {
      this.acquisitionStatus.set(value);
    });

    this.form.controls.gameStatus.valueChanges.subscribe((value) => {
      if (value === 'Completed') {
        this.form.controls.progressPercentage.setValue(100, { emitEvent: false });
      }
    });

    effect(() => {
      const game = this.game();
      if (game === null) {
        return;
      }
      this.form.controls.name.setValue(game.name, { emitEvent: false });
      this.form.controls.coverImageUrl.setValue(game.coverImageUrl, { emitEvent: false });
      this.form.controls.acquisitionStatus.setValue(game.acquisitionStatus);
      this.form.controls.platformIds.setValue([...game.platformIds], { emitEvent: false });
      this.form.controls.genreIds.setValue([...game.genreIds], { emitEvent: false });
      this.form.controls.gameStatus.setValue(game.gameStatus, { emitEvent: false });
      this.form.controls.progressPercentage.setValue(game.progressPercentage, { emitEvent: false });
      this.form.controls.rating.setValue(game.rating, { emitEvent: false });
      this.form.controls.notes.setValue(game.notes, { emitEvent: false });
      this.form.controls.minimumPlayers.setValue(game.minimumPlayers, { emitEvent: false });
      this.form.controls.maximumPlayers.setValue(game.maximumPlayers, { emitEvent: false });
    });

    effect(() => {
      if (!this.isOwned()) {
        this.form.controls.gameStatus.setValue(null, { emitEvent: false });
        this.form.controls.progressPercentage.setValue(null, { emitEvent: false });
      }
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
    const owned = this.isOwned();
    this.submitted.emit({
      name: f.name.value.trim(),
      coverImageUrl: trimToNull(f.coverImageUrl.value),
      acquisitionStatus: f.acquisitionStatus.value,
      platformIds: f.platformIds.value,
      genreIds: f.genreIds.value,
      gameStatus: owned ? f.gameStatus.value : null,
      progressPercentage: owned ? f.progressPercentage.value : null,
      rating: f.rating.value,
      notes: trimToNull(f.notes.value),
      minimumPlayers: f.minimumPlayers.value,
      maximumPlayers: f.maximumPlayers.value,
    });
  }

  togglePlatform(platformId: string, checked: boolean): void {
    const selected = new Set(this.form.controls.platformIds.value);
    if (checked) {
      selected.add(platformId);
    } else {
      selected.delete(platformId);
    }
    this.form.controls.platformIds.setValue([...selected]);
  }

  toggleGenre(genreId: string, checked: boolean): void {
    const selected = new Set(this.form.controls.genreIds.value);
    if (checked) {
      selected.add(genreId);
    } else {
      selected.delete(genreId);
    }
    this.form.controls.genreIds.setValue([...selected]);
  }

  private loadPlatforms(): void {
    this.platformsService.list().subscribe({
      next: (platforms) => {
        this.platforms.set(platforms);
        this.preselectSinglePlatformForQuickAdd();
      },
      error: () => this.loadError.set(true),
    });
  }

  private loadGenres(): void {
    this.genresService.list().subscribe({
      next: (genres) => this.genres.set(genres),
      error: () => this.loadError.set(true),
    });
  }

  private preselectSinglePlatformForQuickAdd(): void {
    if (this.game() !== null) {
      return;
    }
    if (this.isOwned() && this.platforms().length === 1) {
      this.form.controls.platformIds.setValue([this.platforms()[0].id]);
    }
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

    const progress = f.progressPercentage.value;
    if (progress !== null && (Number.isNaN(progress) || progress < 0 || progress > 100)) {
      f.progressPercentage.markAsTouched();
      f.progressPercentage.setErrors({ range: true });
      return '';
    }
    f.progressPercentage.setErrors(null);

    const minimumPlayers = f.minimumPlayers.value;
    const maximumPlayers = f.maximumPlayers.value;
    if ((minimumPlayers === null) !== (maximumPlayers === null)) {
      f.minimumPlayers.markAsTouched();
      f.maximumPlayers.markAsTouched();
      f.minimumPlayers.setErrors({ paired: true });
      f.maximumPlayers.setErrors({ paired: true });
      return 'Minimum and maximum players must both be provided.';
    }
    if (minimumPlayers !== null && (Number.isNaN(minimumPlayers) || minimumPlayers < 1)) {
      f.minimumPlayers.markAsTouched();
      f.minimumPlayers.setErrors({ range: true });
      return '';
    }
    if (maximumPlayers !== null && minimumPlayers !== null && maximumPlayers < minimumPlayers) {
      f.maximumPlayers.markAsTouched();
      f.maximumPlayers.setErrors({ range: true });
      return '';
    }
    f.minimumPlayers.setErrors(null);
    f.maximumPlayers.setErrors(null);

    if (this.isOwned() && f.platformIds.value.length === 0) {
      if (this.platforms().length === 0) {
        return 'An Owned game requires at least one platform. Add one in the Platforms page first.';
      }
      return 'An Owned game requires at least one platform.';
    }

    return null;
  }
}

function trimToNull(value: string | null | undefined): string | null {
  const trimmed = value?.trim() ?? '';
  return trimmed.length === 0 ? null : trimmed;
}
