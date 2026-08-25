import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, computed, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { Genre } from '../../games/genres';
import { GenresService } from '../../games/genres.service';
import type { AcquisitionStatus } from '../../games/acquisition-status';
import type { InteractionType } from '../../games/board-game';
import type { GameStatus } from '../../games/video-game';
import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';
import { CreatePlayLogEntryRequest } from '../../play-log/play-log';
import { LogPlayDialog } from '../../play-log/log-play-dialog/log-play-dialog';
import { PlayLogService } from '../../play-log/play-log.service';

import { LibraryFilterState, LibraryGameType, LibraryItem, LibrarySort } from '../library';
import { LibraryService } from '../library.service';

const ACQUISITION_STATUSES: AcquisitionStatus[] = ['Owned', 'Wishlist', 'Interested'];
const GAME_STATUSES: GameStatus[] = ['Backlog', 'Playing', 'Completed', 'Abandoned', 'WantToPlay'];
const INTERACTION_TYPES: InteractionType[] = ['Cooperative', 'Competitive'];
const SORTS: { value: LibrarySort; label: string }[] = [
  { value: 'NameAsc', label: 'Name A-Z' },
  { value: 'NameDesc', label: 'Name Z-A' },
  { value: 'RatingDesc', label: 'Rating highest first' },
  { value: 'RatingAsc', label: 'Rating lowest first' },
  { value: 'RecentlyAdded', label: 'Recently added' },
];

@Component({
  selector: 'app-library-page',
  imports: [RouterLink, LogPlayDialog],
  templateUrl: './library-page.html',
  styleUrl: './library-page.scss',
})
export class LibraryPage implements OnDestroy {
  private readonly logPlayDialog = viewChild<LogPlayDialog>('logPlayDialog');
  private readonly libraryService = inject(LibraryService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly playLogService = inject(PlayLogService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly items = signal<LibraryItem[]>([]);
  protected readonly error = signal('');
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly filters = signal<LibraryFilterState>(emptyFilters());
  protected readonly sort = signal<LibrarySort>('NameAsc');
  protected readonly filtersOpen = signal(false);
  protected readonly loggingGameIds = signal<string[]>([]);
  protected readonly logSuccessByGameId = signal<Record<string, string>>({});
  protected readonly logErrorByGameId = signal<Record<string, string>>({});

  protected readonly acquisitionStatuses = ACQUISITION_STATUSES;
  protected readonly gameStatuses = GAME_STATUSES;
  protected readonly interactionTypes = INTERACTION_TYPES;
  protected readonly sorts = SORTS;

  protected readonly showVideoFilters = computed(() => this.filters().gameType !== 'BoardGame');
  protected readonly showBoardFilters = computed(() => this.filters().gameType !== 'VideoGame');
  protected readonly hasActiveCriteria = computed(() => hasActiveCriteria(this.filters()));
  protected readonly filterSummary = computed(() => filterSummary(this.filters(), this.sort()));

  private requestId = 0;
  private searchDebounce: ReturnType<typeof setTimeout> | null = null;

  constructor() {
    this.loadLookups();
    this.load();
  }

  ngOnDestroy(): void {
    if (this.searchDebounce !== null) {
      clearTimeout(this.searchDebounce);
    }
  }

  load(): void {
    const id = ++this.requestId;
    this.loading.set(true);
    this.error.set('');

    this.libraryService.browse(this.filters(), this.sort()).subscribe({
      next: (items) => {
        if (id !== this.requestId) {
          return;
        }
        this.items.set(items);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (id !== this.requestId) {
          return;
        }
        this.loading.set(false);
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.error.set(this.errorMessage(error));
      },
    });
  }

  protected onSearch(value: string): void {
    this.patchFilters({ search: value });
    if (this.searchDebounce !== null) {
      clearTimeout(this.searchDebounce);
    }
    this.searchDebounce = setTimeout(() => this.load(), 200);
  }

  protected onGameType(value: string): void {
    this.patchFilters({ gameType: value === '' ? null : (value as LibraryGameType) });
    this.load();
  }

  protected onRatingMin(value: string): void {
    this.patchFilters({ ratingMin: value === '' ? null : Number(value) });
    this.load();
  }

  protected onPlayerCount(value: string): void {
    this.patchFilters({ playerCount: value === '' ? null : Number(value) });
    this.load();
  }

  protected onAcquisitionStatuses(event: Event): void {
    this.patchFilters({ acquisitionStatuses: selectedValues(event) as AcquisitionStatus[] });
    this.load();
  }

  protected onPlatformIds(event: Event): void {
    this.patchFilters({ platformIds: selectedValues(event) });
    this.load();
  }

  protected onGenreIds(event: Event): void {
    this.patchFilters({ genreIds: selectedValues(event) });
    this.load();
  }

  protected onGameStatuses(event: Event): void {
    this.patchFilters({ gameStatuses: selectedValues(event) as GameStatus[] });
    this.load();
  }

  protected onInteractionTypes(event: Event): void {
    this.patchFilters({ interactionTypes: selectedValues(event) as InteractionType[] });
    this.load();
  }

  protected onSort(value: string): void {
    this.sort.set(value as LibrarySort);
    this.load();
  }

  protected clearFilters(): void {
    this.filters.set(emptyFilters());
    this.load();
  }

  protected toggleFilters(): void {
    this.filtersOpen.update((open) => !open);
  }

  protected edit(item: LibraryItem): void {
    void this.router.navigateByUrl(item.gameType === 'VideoGame' ? '/video-games' : '/board-games');
  }

  protected logPlay(item: LibraryItem): void {
    if (item.acquisitionStatus !== 'Owned' || this.isLogging(item.id)) {
      return;
    }

    this.logSuccessByGameId.update((messages) => withoutKey(messages, item.id));
    this.logErrorByGameId.update((messages) => withoutKey(messages, item.id));
    this.logPlayDialog()?.open({ id: item.id, name: item.name });
  }

  protected onLogPlayConfirmed(request: CreatePlayLogEntryRequest): void {
    if (this.isLogging(request.gameId)) {
      return;
    }

    this.loggingGameIds.update((ids) => [...ids, request.gameId]);
    this.logSuccessByGameId.update((messages) => withoutKey(messages, request.gameId));
    this.logErrorByGameId.update((messages) => withoutKey(messages, request.gameId));
    this.logPlayDialog()?.setPending(true);

    this.playLogService.logPlay(request).subscribe({
      next: () => {
        this.loggingGameIds.update((ids) => ids.filter((id) => id !== request.gameId));
        this.logSuccessByGameId.update((messages) => ({ ...messages, [request.gameId]: 'Play logged.' }));
        this.logPlayDialog()?.close();
      },
      error: (error: HttpErrorResponse) => {
        this.loggingGameIds.update((ids) => ids.filter((id) => id !== request.gameId));
        if (error.status === 401) {
          this.logPlayDialog()?.close();
          this.handleSessionExpired();
          return;
        }
        const message = this.logErrorMessage(error);
        this.logErrorByGameId.update((messages) => ({ ...messages, [request.gameId]: message }));
        this.logPlayDialog()?.showError(message);
      },
    });
  }

  protected isLogging(gameId: string): boolean {
    return this.loggingGameIds().includes(gameId);
  }

  protected platformNames(item: LibraryItem): string {
    return item.platformIds.map((id) => this.platformName(id)).join(', ');
  }

  protected genreNames(item: LibraryItem): string {
    return item.genreIds.map((id) => this.genreName(id)).join(', ');
  }

  protected playerRange(item: LibraryItem): string {
    if (item.minimumPlayers === null || item.maximumPlayers === null) {
      return '';
    }
    if (item.minimumPlayers === 1 && item.maximumPlayers === 1) {
      return '1 player';
    }
    return `${item.minimumPlayers}-${item.maximumPlayers} players`;
  }

  private patchFilters(patch: Partial<LibraryFilterState>): void {
    this.filters.update((current) => ({ ...current, ...patch }));
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
    this.error.set('Unable to load your library. Please try again.');
    this.loading.set(false);
  }

  private errorMessage(error: HttpErrorResponse): string {
    if (error.status === 400) {
      const detail = serverDetail(error);
      return detail === null ? 'Invalid filters.' : `Invalid filters: ${detail}`;
    }
    return 'Unable to load your library. Please try again.';
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

function emptyFilters(): LibraryFilterState {
  return {
    search: '',
    gameType: null,
    acquisitionStatuses: [],
    platformIds: [],
    genreIds: [],
    ratingMin: null,
    playerCount: null,
    interactionTypes: [],
    gameStatuses: [],
  };
}

function hasActiveCriteria(filters: LibraryFilterState): boolean {
  return (
    filters.search.trim().length > 0 ||
    filters.gameType !== null ||
    filters.acquisitionStatuses.length > 0 ||
    filters.platformIds.length > 0 ||
    filters.genreIds.length > 0 ||
    filters.ratingMin !== null ||
    filters.playerCount !== null ||
    filters.interactionTypes.length > 0 ||
    filters.gameStatuses.length > 0
  );
}

function filterSummary(filters: LibraryFilterState, sort: LibrarySort): string {
  const active = activeFilterCount(filters);
  const sortLabel = SORTS.find((option) => option.value === sort)?.label ?? 'Custom sort';
  if (active === 0) {
    return `No filters active. Sorted by ${sortLabel}.`;
  }
  return `${active} filter${active === 1 ? '' : 's'} active. Sorted by ${sortLabel}.`;
}

function activeFilterCount(filters: LibraryFilterState): number {
  return [
    filters.search.trim().length > 0,
    filters.gameType !== null,
    filters.acquisitionStatuses.length > 0,
    filters.platformIds.length > 0,
    filters.genreIds.length > 0,
    filters.ratingMin !== null,
    filters.playerCount !== null,
    filters.interactionTypes.length > 0,
    filters.gameStatuses.length > 0,
  ].filter(Boolean).length;
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

function withoutKey<T>(source: Record<string, T>, key: string): Record<string, T> {
  const rest = { ...source };
  delete rest[key];
  return rest;
}
