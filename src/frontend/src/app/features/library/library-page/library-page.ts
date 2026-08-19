import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { Genre } from '../../games/genres';
import { GenresService } from '../../games/genres.service';
import type { AcquisitionStatus } from '../../games/acquisition-status';
import type { InteractionType } from '../../games/board-game';
import type { GameStatus } from '../../games/video-game';
import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';

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
  imports: [RouterLink],
  templateUrl: './library-page.html',
  styleUrl: './library-page.scss',
})
export class LibraryPage implements OnDestroy {
  private readonly libraryService = inject(LibraryService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly items = signal<LibraryItem[]>([]);
  protected readonly error = signal('');
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly filters = signal<LibraryFilterState>(emptyFilters());
  protected readonly sort = signal<LibrarySort>('NameAsc');

  protected readonly acquisitionStatuses = ACQUISITION_STATUSES;
  protected readonly gameStatuses = GAME_STATUSES;
  protected readonly interactionTypes = INTERACTION_TYPES;
  protected readonly sorts = SORTS;

  protected readonly showVideoFilters = computed(() => this.filters().gameType !== 'BoardGame');
  protected readonly showBoardFilters = computed(() => this.filters().gameType !== 'VideoGame');
  protected readonly hasActiveCriteria = computed(() => hasActiveCriteria(this.filters()));

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

  protected edit(item: LibraryItem): void {
    void this.router.navigateByUrl(item.gameType === 'VideoGame' ? '/video-games' : '/board-games');
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
