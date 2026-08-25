import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import type { InteractionType } from '../../games/board-game';
import { Genre } from '../../games/genres';
import { GenresService } from '../../games/genres.service';
import type { GameStatus } from '../../games/video-game';
import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';

import {
  RandomPickerFilters,
  RandomPickerItem,
  RandomPickerMode,
  RandomPickerPickRequest,
  RandomPickerState,
} from '../random-picker';
import { RandomPickerService } from '../random-picker.service';

const GAME_STATUSES: GameStatus[] = ['Backlog', 'Playing', 'Completed', 'Abandoned', 'WantToPlay'];
const INTERACTION_TYPES: InteractionType[] = ['Cooperative', 'Competitive'];
const MODES: { value: RandomPickerMode; label: string }[] = [
  { value: 'All', label: 'All' },
  { value: 'VideoGames', label: 'Video games' },
  { value: 'BoardGames', label: 'Board games' },
];

@Component({
  selector: 'app-random-picker-page',
  imports: [],
  templateUrl: './random-picker-page.html',
  styleUrl: './random-picker-page.scss',
})
export class RandomPickerPage {
  private readonly randomPicker = inject(RandomPickerService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly mode = signal<RandomPickerMode>('All');
  protected readonly filters = signal<RandomPickerFilters>(emptyFilters());
  protected readonly currentResult = signal<RandomPickerItem | null>(null);
  protected readonly currentState = signal<RandomPickerState | null>(null);
  protected readonly visibleHistory = signal<RandomPickerItem[]>([]);
  protected readonly loading = signal(false);
  protected readonly error = signal('');
  protected readonly shownLibraryEntryIds = signal<string[]>([]);
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly filtersOpen = signal(false);
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
    this.loadLookups();
  }

  protected pick(): void {
    this.sendPick();
  }

  protected another(): void {
    this.sendPick();
  }

  protected onMode(value: string): void {
    const nextMode = value as RandomPickerMode;
    this.mode.set(nextMode);
    this.filters.update((current) => filtersForMode(nextMode, current));
    this.clearDisplayedResult();
  }

  protected onRatingMin(value: string): void {
    this.patchFilters({ ratingMin: value === '' ? null : Number(value) });
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
    this.filters.set(filtersForMode(this.mode(), emptyFilters()));
    this.clearDisplayedResult();
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected resetShownHistory(): void {
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
    this.loading.set(true);
    this.error.set('');

    this.randomPicker.pick(this.request()).subscribe({
      next: (response) => {
        this.loading.set(false);
        this.currentState.set(response.state);
        this.currentResult.set(response.result);
        if (response.result !== null) {
          this.visibleHistory.update((history) => [response.result!, ...history.filter((item) => item.libraryEntryId !== response.result!.libraryEntryId)]);
          this.shownLibraryEntryIds.update((ids) =>
            ids.includes(response.result!.libraryEntryId) ? ids : [...ids, response.result!.libraryEntryId],
          );
        }
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.error.set(this.errorMessage(error));
      },
    });
  }

  private request(): RandomPickerPickRequest {
    const filters = filtersForMode(this.mode(), this.filters());
    return {
      mode: this.mode(),
      shownLibraryEntryIds: this.shownLibraryEntryIds(),
      ...filters,
    };
  }

  private patchFilters(patch: Partial<RandomPickerFilters>): void {
    this.filters.update((current) => ({ ...current, ...patch }));
    this.clearDisplayedResult();
  }

  private clearDisplayedResult(): void {
    this.currentResult.set(null);
    this.currentState.set(null);
    this.error.set('');
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
  };
}

function filtersForMode(mode: RandomPickerMode, current: RandomPickerFilters): RandomPickerFilters {
  const empty = emptyFilters();
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
    ].filter(Boolean).length;
  }
  if (mode === 'BoardGames') {
    return [filters.playerCount !== null, filters.availableDuration !== null, filters.interactionTypes.length > 0].filter(Boolean).length;
  }
  return [filters.ratingMin !== null, filters.playerCount !== null].filter(Boolean).length;
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
