import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { Observable, takeUntil } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { CurrentlyPlayingService } from '../../currently-playing/currently-playing.service';
import { CoverImage } from '../../games/cover-image/cover-image';
import type { InteractionType } from '../../games/board-game';
import { Genre } from '../../games/genres';
import { GenresService } from '../../games/genres.service';
import type { GameStatus } from '../../games/video-game';
import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';
import { CreatePlayLogEntryRequest } from '../../play-log/play-log';
import { LogPlayDialog } from '../../play-log/log-play-dialog/log-play-dialog';
import { PlayLogService } from '../../play-log/play-log.service';

import {
  RandomPickerFilters,
  RandomPickerItem,
  RandomPickerMode,
  PickerRound,
  RandomPickerState,
} from '../random-picker';
import { PickerRoundService } from '../picker-round.service';

const GAME_STATUSES: GameStatus[] = ['Backlog', 'Playing', 'Completed', 'Abandoned', 'WantToPlay'];
const INTERACTION_TYPES: InteractionType[] = ['Cooperative', 'Competitive'];
const MODES: { value: RandomPickerMode; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'VideoGames', label: 'Video games' },
  { value: 'BoardGames', label: 'Board games' },
];

@Component({
  selector: 'app-random-picker-page',
  imports: [LogPlayDialog, CoverImage],
  templateUrl: './random-picker-page.html',
  styleUrl: './random-picker-page.scss',
})
export class RandomPickerPage {
  private readonly logPlayDialog = viewChild<LogPlayDialog>('logPlayDialog');
  private readonly rounds = inject(PickerRoundService);
  private readonly currentlyPlaying = inject(CurrentlyPlayingService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly playLogService = inject(PlayLogService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private pickGeneration = 0;

  protected readonly mode = signal<RandomPickerMode>('All');
  protected readonly filters = signal<RandomPickerFilters>(emptyFilters());
  protected readonly currentResult = signal<RandomPickerItem | null>(null);
  protected readonly currentState = signal<RandomPickerState | null>(null);
  protected readonly visibleHistory = signal<RandomPickerItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly ready = signal(false);
  protected readonly proposalUnavailable = signal(false);
  protected readonly error = signal('');
  protected readonly shownLibraryEntryIds = signal<string[]>([]);
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly filtersOpen = signal(false);
  protected readonly logPendingGameId = signal<string | null>(null);
  protected readonly logSuccess = signal('');
  protected readonly logError = signal('');
  protected readonly selectionPending = signal(false);
  protected readonly selectedGameId = signal<string | null>(null);
  protected readonly selectionError = signal('');
  protected readonly filterSummary = computed(() => filterSummary(this.mode(), this.filters()));
  protected readonly historyItems = computed(() => {
    const current = this.currentResult();
    if (current === null) {
      return this.visibleHistory();
    }
    return this.visibleHistory().filter((item) => item.libraryEntryId !== current.libraryEntryId);
  });

  protected readonly modes = MODES;
  protected readonly gameStatuses = GAME_STATUSES;
  protected readonly interactionTypes = INTERACTION_TYPES;

  constructor() {
    this.auth.subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.clearLocalRound();
      this.selectionPending.set(false);
      this.selectedGameId.set(null);
      this.selectionError.set('');
      if (this.auth.isAuthenticated()) {
        this.mode.set('All');
        this.reloadRound();
      }
    });
    this.loadLookups();
    this.reloadRound();
  }

  protected selectGame(item: RandomPickerItem): void {
    if (this.selectionPending() || this.selectedGameId() === item.gameId) return;
    this.selectionPending.set(true);
    this.selectionError.set('');
    this.currentlyPlaying.select(item.gameId)
      .pipe(takeUntilDestroyed(this.destroyRef), takeUntil(this.auth.subjectChanges))
      .subscribe({
        next: () => {
          this.selectionPending.set(false);
          this.selectedGameId.set(item.gameId);
        },
        error: (error: HttpErrorResponse) => {
          this.selectionPending.set(false);
          if (error.status === 401) {
            this.handleSessionExpired();
            return;
          }
          if (this.currentResult()?.gameId === item.gameId) {
            this.selectionError.set(error.status === 404 || error.status === 409
              ? 'This game is no longer available to select. Pick another game.'
              : 'Unable to confirm selection. Check Currently Playing on Home before selecting again.');
          }
        },
      });
  }

  protected pick(): void {
    this.sendPick();
  }

  protected another(): void {
    this.sendPick();
  }

  protected onMode(value: string): void {
    const nextMode = value as RandomPickerMode;
    if (nextMode === this.mode()) return;
    this.mode.set(nextMode);
    this.reloadRound();
  }

  protected onRatingMin(value: string): void {
    this.patchFilters({ ratingMin: value === '' ? null : Number(value) });
  }

  protected onFamilyFriendlyOnly(checked: boolean): void {
    this.patchFilters({ familyFriendlyOnly: checked });
  }

  protected onPlayerCount(value: string): void {
    this.patchFilters({ playerCount: value === '' ? null : Number(value) });
  }

  protected onAvailableDuration(value: string): void {
    this.patchFilters({ availableDuration: value === '' ? null : Number(value) });
  }

  protected onPlatformIds(event: Event): void {
    this.patchFilters({ platformIds: selectedValues(event) });
  }

  protected onGenreIds(event: Event): void {
    this.patchFilters({ genreIds: selectedValues(event) });
  }

  protected onGameStatuses(event: Event): void {
    this.patchFilters({ gameStatuses: selectedValues(event) as GameStatus[] });
  }

  protected onInteractionTypes(event: Event): void {
    this.patchFilters({ interactionTypes: selectedValues(event) as InteractionType[] });
  }

  protected clearFilters(): void {
    if (!this.ready()) return;
    this.filters.set(filtersForMode(this.mode(), emptyFilters()));
    this.clearDisplayedResult();
    this.saveFilters();
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected resetShownHistory(): void {
    if (!this.ready() || this.loading()) return;
    this.loading.set(true);
    this.observeRound(this.rounds.reset(this.mode()), ++this.pickGeneration);
  }

  protected reloadRound(): void {
    this.clearLocalRound();
    this.loading.set(true);
    this.observeRound(this.rounds.load(this.mode()), ++this.pickGeneration);
  }

  private clearLocalRound(): void {
    this.ready.set(false);
    this.filters.set(emptyFilters());
    this.shownLibraryEntryIds.set([]);
    this.visibleHistory.set([]);
    this.clearDisplayedResult();
  }

  protected metadata(item: RandomPickerItem): string[] {
    const details: string[] = [];
    const players = this.playerRange(item);
    if (players) {
      details.push(players);
    }
    if (item.rating !== null) {
      details.push(`Rating: ${item.rating}`);
    }
    if (item.gameType === 'VideoGame') {
      if (item.gameStatus !== null) {
        details.push(item.gameStatus);
      }
      if (item.progressPercentage !== null) {
        details.push(`${item.progressPercentage}% progress`);
      }
      return details;
    }
    if (item.approximateDuration !== null) {
      details.push(`${item.approximateDuration} min`);
    }
    if (item.interactionType !== null) {
      details.push(item.interactionType);
    }
    return details;
  }

  protected edit(item: RandomPickerItem): void {
    void this.router.navigateByUrl(item.gameType === 'VideoGame' ? '/video-games' : '/board-games');
  }

  protected logPlay(item: RandomPickerItem): void {
    const gameId = item.gameId;
    if (this.logPendingGameId() === gameId) {
      return;
    }

    this.logSuccess.set('');
    this.logError.set('');
    this.logPlayDialog()?.open({ id: gameId, name: item.name });
  }

  protected onLogPlayConfirmed(request: CreatePlayLogEntryRequest): void {
    const gameId = request.gameId;
    if (this.logPendingGameId() === gameId) {
      return;
    }

    this.logPendingGameId.set(gameId);
    this.logSuccess.set('');
    this.logError.set('');
    this.logPlayDialog()?.setPending(true);

    this.playLogService.logPlay(request).subscribe({
      next: () => {
        if (this.logPendingGameId() === gameId) {
          this.logPendingGameId.set(null);
        }
        this.logPlayDialog()?.close();
        if (this.currentResult()?.gameId === gameId) {
          this.logSuccess.set('Play logged.');
        }
      },
      error: (error: HttpErrorResponse) => {
        if (this.logPendingGameId() === gameId) {
          this.logPendingGameId.set(null);
        }
        if (error.status === 401) {
          this.logPlayDialog()?.close();
          this.handleSessionExpired();
          return;
        }
        if (this.currentResult()?.gameId === gameId) {
          const message = this.logErrorMessage(error);
          this.logError.set(message);
          this.logPlayDialog()?.showError(message);
        }
      },
    });
  }

  protected isLogging(item: RandomPickerItem): boolean {
    return this.logPendingGameId() === item.gameId;
  }

  protected platformNames(item: RandomPickerItem): string {
    return item.platformIds.map((id) => this.platformName(id)).join(', ');
  }

  protected genreNames(item: RandomPickerItem): string {
    return item.genreIds.map((id) => this.genreName(id)).join(', ');
  }

  protected playerRange(item: RandomPickerItem): string {
    if (item.minimumPlayers === null || item.maximumPlayers === null) {
      return '';
    }
    if (item.minimumPlayers === 1 && item.maximumPlayers === 1) {
      return '1 player';
    }
    return `${item.minimumPlayers}-${item.maximumPlayers} players`;
  }

  private sendPick(): void {
    if (!this.ready() || this.loading()) return;
    const generation = ++this.pickGeneration;
    this.loading.set(true);
    this.error.set('');

    this.observeRound(this.rounds.pick(this.mode(), filtersForMode(this.mode(), this.filters())), generation);
  }

  private observeRound(request: Observable<PickerRound>, generation: number): void {
    request.pipe(takeUntilDestroyed(this.destroyRef), takeUntil(this.auth.subjectChanges)).subscribe({
      next: (round) => {
        if (generation !== this.pickGeneration) {
          return;
        }
        this.loading.set(false);
        this.ready.set(true);
        this.filters.set(round.filters);
        this.currentState.set(round.state);
        this.currentResult.set(round.result);
        this.visibleHistory.set(round.history);
        this.shownLibraryEntryIds.set(round.shownLibraryEntryIds);
        this.proposalUnavailable.set(round.proposalUnavailable);
        this.selectionError.set('');
        this.logPendingGameId.set(null);
        this.logSuccess.set('');
        this.logError.set('');
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.pickGeneration) {
          return;
        }
        this.loading.set(false);
        this.ready.set(false);
        this.currentResult.set(null);
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.error.set(error.status === 409
          ? 'This round changed elsewhere. Reload round before continuing.'
          : `${this.errorMessage(error)} Reload round before continuing.`);
      },
    });
  }

  private saveFilters(): void {
    this.observeRound(this.rounds.saveFilters(this.mode(), filtersForMode(this.mode(), this.filters())), this.pickGeneration);
  }

  private patchFilters(patch: Partial<RandomPickerFilters>): void {
    if (!this.ready()) return;
    this.filters.update((current) => ({ ...current, ...patch }));
    this.clearDisplayedResult();
    this.saveFilters();
  }

  private clearDisplayedResult(): void {
    // Pending picks no longer describe the current filters or shown-history pool.
    this.pickGeneration++;
    this.loading.set(false);
    this.currentResult.set(null);
    this.currentState.set(null);
    this.proposalUnavailable.set(false);
    this.selectionError.set('');
    this.error.set('');
    this.logPendingGameId.set(null);
    this.logSuccess.set('');
    this.logError.set('');
  }

  private loadLookups(): void {
    this.platformsService.list().subscribe({
      next: (platforms) => this.platforms.set(platforms),
      error: (error: HttpErrorResponse) => this.handleLookupError(error),
    });
    this.genresService.list().subscribe({
      next: (genres) => this.genres.set(genres),
      error: (error: HttpErrorResponse) => this.handleLookupError(error),
    });
  }

  private platformName(id: string): string {
    return this.platforms().find((platform) => platform.id === id)?.name ?? 'Unknown platform';
  }

  private genreName(id: string): string {
    return this.genres().find((genre) => genre.id === id)?.name ?? 'Unknown genre';
  }

  private handleLookupError(error: HttpErrorResponse): void {
    if (error.status === 401) {
      this.handleSessionExpired();
      return;
    }
    this.error.set('Unable to load Random Picker data. Please try again.');
  }

  private errorMessage(error: HttpErrorResponse): string {
    if (error.status === 400) {
      const detail = serverDetail(error);
      return detail === null ? 'Invalid picker filters.' : `Invalid picker filters: ${detail}`;
    }
    return 'Unable to pick a game. Please try again.';
  }

  private logErrorMessage(error: HttpErrorResponse): string {
    const detail = serverDetail(error);
    if (detail !== null) {
      return detail;
    }
    return 'Unable to log this play. Please try again.';
  }

  private handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}

function emptyFilters(): RandomPickerFilters {
  return {
    platformIds: [],
    genreIds: [],
    gameStatuses: [],
    playerCount: null,
    availableDuration: null,
    interactionTypes: [],
    ratingMin: null,
    familyFriendlyOnly: false,
  };
}

function filtersForMode(mode: RandomPickerMode, current: RandomPickerFilters): RandomPickerFilters {
  const empty = { ...emptyFilters(), familyFriendlyOnly: current.familyFriendlyOnly };
  if (mode === 'VideoGames') {
    return {
      ...empty,
      platformIds: current.platformIds,
      genreIds: current.genreIds,
      gameStatuses: current.gameStatuses,
      playerCount: current.playerCount,
    };
  }
  if (mode === 'BoardGames') {
    return {
      ...empty,
      playerCount: current.playerCount,
      availableDuration: current.availableDuration,
      interactionTypes: current.interactionTypes,
    };
  }
  return {
    ...empty,
    ratingMin: current.ratingMin,
    playerCount: current.playerCount,
  };
}

function filterSummary(mode: RandomPickerMode, filters: RandomPickerFilters): string {
  const active = activeFilterCount(mode, filters);
  if (active === 0) {
    return 'No picker filters active.';
  }
  return `${active} picker filter${active === 1 ? '' : 's'} active.`;
}

function activeFilterCount(mode: RandomPickerMode, filters: RandomPickerFilters): number {
  if (mode === 'VideoGames') {
    return [
      filters.platformIds.length > 0,
      filters.genreIds.length > 0,
      filters.gameStatuses.length > 0,
      filters.playerCount !== null,
      filters.familyFriendlyOnly,
    ].filter(Boolean).length;
  }
  if (mode === 'BoardGames') {
    return [filters.playerCount !== null, filters.availableDuration !== null, filters.interactionTypes.length > 0, filters.familyFriendlyOnly].filter(Boolean).length;
  }
  return [filters.ratingMin !== null, filters.playerCount !== null, filters.familyFriendlyOnly].filter(Boolean).length;
}

function selectedValues(event: Event): string[] {
  const select = event.target as HTMLSelectElement;
  return [...select.selectedOptions].map((option) => option.value);
}

function serverDetail(error: HttpErrorResponse): string | null {
  const body = error.error as { detail?: unknown } | null;
  if (body !== null && typeof body === 'object' && typeof body.detail === 'string') {
    return body.detail;
  }
  return null;
}
