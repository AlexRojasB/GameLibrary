import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, effect, inject, input, output, signal } from '@angular/core';

import { CoverImageCandidate, CoverImageSearchGameType, CoverImageSearchService } from './cover-image-search.service';

@Component({
  selector: 'app-cover-image-search',
  templateUrl: './cover-image-search.html',
  styleUrl: './cover-image-search.scss',
})
export class CoverImageSearch {
  readonly gameType = input.required<CoverImageSearchGameType>();
  readonly gameName = input('');
  readonly coverImageUrl = input<string | null>(null);
  readonly selectedPlatformNames = input<string[]>([]);

  readonly coverImageUrlSelected = output<string>();
  readonly unauthorized = output<void>();

  private readonly service = inject(CoverImageSearchService);
  private lastContextKey: string | null = null;
  private searchGeneration = 0;

  protected readonly loading = signal(false);
  protected readonly candidates = signal<CoverImageCandidate[]>([]);
  protected readonly selectedCandidate = signal<CoverImageCandidate | null>(null);
  protected readonly error = signal('');
  protected readonly noResults = signal(false);
  protected readonly brokenPreviewUrls = signal<ReadonlySet<string>>(new Set());
  protected readonly searchPlatformName = signal<string | null>(null);

  protected readonly trimmedName = computed(() => this.gameName().trim());
  protected readonly platformOptions = computed(() => this.selectedPlatformNames());
  protected readonly showPlatformSelector = computed(() => this.gameType() === 'VideoGame' && this.platformOptions().length > 1);
  protected readonly effectivePlatformName = computed(() => {
    if (this.gameType() !== 'VideoGame') {
      return null;
    }
    const platforms = this.platformOptions();
    if (platforms.length === 0) {
      return null;
    }
    if (platforms.length === 1) {
      return platforms[0];
    }
    return this.searchPlatformName() ?? platforms[0];
  });
  protected readonly canSearch = computed(() => this.trimmedName().length > 0 && !this.loading());
  protected readonly contextKey = computed(() => `${this.gameType()}|${this.trimmedName()}|${this.effectivePlatformName() ?? ''}`);

  constructor() {
    effect(() => {
      const options = this.platformOptions();
      if (options.length <= 1) {
        this.searchPlatformName.set(options[0] ?? null);
        return;
      }
      if (!this.searchPlatformName() || !options.includes(this.searchPlatformName()!)) {
        this.searchPlatformName.set(options[0]);
      }
    });

    effect(() => {
      const key = this.contextKey();
      if (this.lastContextKey === null) {
        this.lastContextKey = key;
        return;
      }
      if (this.lastContextKey !== key) {
        this.lastContextKey = key;
        this.searchGeneration++;
        this.clearSearchState();
      }
    });

    effect(() => {
      const selected = this.selectedCandidate();
      if (selected !== null && this.coverImageUrl() !== selected.imageUrl) {
        this.selectedCandidate.set(null);
      }
    });
  }

  protected search(): void {
    if (this.loading()) {
      return;
    }

    const name = this.trimmedName();
    if (name.length === 0) {
      this.error.set('Enter a game name before searching.');
      return;
    }

    const generation = ++this.searchGeneration;
    this.loading.set(true);
    this.candidates.set([]);
    this.selectedCandidate.set(null);
    this.error.set('');
    this.noResults.set(false);
    this.brokenPreviewUrls.set(new Set());

    const platformName = this.effectivePlatformName();
    this.service
      .search({
        gameType: this.gameType(),
        name,
        ...(this.gameType() === 'VideoGame' && platformName !== null ? { platformName } : {}),
      })
      .subscribe({
        next: (response) => {
          if (generation !== this.searchGeneration) {
            return;
          }
          const candidates = response.candidates.slice(0, 5);
          this.candidates.set(candidates);
          this.noResults.set(candidates.length === 0);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          if (generation !== this.searchGeneration) {
            return;
          }
          this.candidates.set([]);
          this.noResults.set(false);
          this.loading.set(false);
          if (error.status === 401) {
            this.unauthorized.emit();
            return;
          }
          this.error.set(searchErrorMessage(error));
        },
      });
  }

  protected selectCandidate(candidate: CoverImageCandidate): void {
    if (this.isPreviewBroken(candidate)) {
      return;
    }
    this.selectedCandidate.set(candidate);
    this.coverImageUrlSelected.emit(candidate.imageUrl);
  }

  protected previewUrl(candidate: CoverImageCandidate): string {
    return candidate.thumbnailUrl ?? candidate.imageUrl;
  }

  protected isSelected(candidate: CoverImageCandidate): boolean {
    return this.selectedCandidate()?.imageUrl === candidate.imageUrl;
  }

  protected isPreviewBroken(candidate: CoverImageCandidate): boolean {
    return this.brokenPreviewUrls().has(candidate.imageUrl);
  }

  protected markPreviewBroken(candidate: CoverImageCandidate): void {
    this.brokenPreviewUrls.update((current) => new Set(current).add(candidate.imageUrl));
  }

  protected changeSearchPlatform(value: string): void {
    this.searchPlatformName.set(value);
  }

  private clearSearchState(): void {
    this.loading.set(false);
    this.candidates.set([]);
    this.selectedCandidate.set(null);
    this.error.set('');
    this.noResults.set(false);
    this.brokenPreviewUrls.set(new Set());
  }
}

function searchErrorMessage(error: HttpErrorResponse): string {
  const detail = typeof error.error?.detail === 'string' ? error.error.detail : null;
  if (detail !== null && detail.length > 0) {
    return detail;
  }
  if (error.status === 429) {
    return 'Too many cover searches. Please wait a minute and try again.';
  }
  return 'Unable to search cover images right now. Please try again.';
}
