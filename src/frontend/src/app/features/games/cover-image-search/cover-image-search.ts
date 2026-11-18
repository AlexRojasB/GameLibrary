import { HttpErrorResponse } from '@angular/common/http';
import { afterNextRender, Component, computed, DestroyRef, effect, inject, Injector, input, output, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { AuthService } from '../../../core/auth/auth.service';
import { CoverImage } from '../cover-image/cover-image';
import { CoverDraft, CoverReference } from '../covers/cover-types';
import { CoverUploadsService } from '../covers/cover-uploads.service';
import { coverSessionKey } from '../covers/cover-session';
import { CoverEditor } from './cover-editor';
import { PREPARE_COVER_IMAGE, PreparedImage } from './image-preparation';

import { CoverImageCandidate, CoverImageSearchGameType, CoverImageSearchService } from './cover-image-search.service';

@Component({
  selector: 'app-cover-image-search',
  imports: [CoverEditor, CoverImage],
  templateUrl: './cover-image-search.html',
  styleUrl: './cover-image-search.scss',
})
export class CoverImageSearch {
  readonly gameType = input.required<CoverImageSearchGameType>();
  readonly gameName = input('');
  readonly draft = input<CoverDraft>({ intent: 'keep' });
  readonly savedCover = input<CoverReference>({ coverImageUrl: null, coverUploadId: null });
  readonly disabled = input(false);
  readonly selectedPlatformNames = input<string[]>([]);

  readonly draftChanged = output<CoverDraft>();
  readonly busyChanged = output<boolean>();
  readonly unauthorized = output<void>();

  private readonly service = inject(CoverImageSearchService);
  private readonly uploads = inject(CoverUploadsService);
  private readonly prepareImage = inject(PREPARE_COVER_IMAGE);
  private readonly auth = inject(AuthService);
  private readonly accountSession = computed(() => coverSessionKey(this.auth.session()));
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  private localGeneration = 0;
  private preparation = new AbortController();
  private destroyed = false;
  private returnFocus: HTMLElement | null = null;
  private readonly invalidatedDraft = signal<CoverDraft | null>(null);
  private lastContextKey: string | null = null;
  private searchGeneration = 0;

  protected readonly loading = signal(false);
  protected readonly candidates = signal<CoverImageCandidate[]>([]);
  protected readonly selectedCandidate = signal<CoverImageCandidate | null>(null);
  protected readonly error = signal('');
  protected readonly noResults = signal(false);
  protected readonly brokenPreviewUrls = signal<ReadonlySet<string>>(new Set());
  protected readonly searchPlatformName = signal<string | null>(null);
  protected readonly uploadsEnabled = signal(false);
  protected readonly uploadStatus = signal('Checking image upload availability...');
  protected readonly preparing = signal(false);
  protected readonly editorImage = signal<PreparedImage | null>(null);
  protected readonly localError = signal('');
  protected readonly localPreviewUrl = signal<string | null>(null);
  protected readonly previewBroken = signal(false);
  protected readonly busy = computed(() => this.preparing() || this.editorImage() !== null);
  protected readonly sourceDisabled = computed(() => this.disabled() || this.busy());
  protected readonly manualUrl = computed(() => {
    const draft = this.draft();
    if (draft === this.invalidatedDraft()) return '';
    return draft.intent === 'external' ? draft.url : draft.intent === 'keep' ? this.savedCover().coverImageUrl ?? '' : '';
  });
  protected readonly selectedUploadId = computed(() => this.draft() !== this.invalidatedDraft() && this.draft().intent === 'keep' ? this.savedCover().coverUploadId : null);

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
  protected readonly canSearch = computed(() => this.trimmedName().length > 0 && !this.loading() && !this.sourceDisabled());
  protected readonly contextKey = computed(() => `${this.gameType()}|${this.trimmedName()}|${this.effectivePlatformName() ?? ''}`);

  protected readonly selectedCoverUrl = computed(() => {
    return this.localPreviewUrl() ?? (this.manualUrl().trim() || null);
  });

  protected readonly hasSelectedCover = computed(() => this.selectedCoverUrl() !== null || this.selectedUploadId() !== null || (this.draft().intent === 'local' && this.draft() !== this.invalidatedDraft()));

  constructor() {
    effect(() => {
      if (this.disabled()) untracked(() => this.finishEditing(false));
    });
    effect((onCleanup) => {
      const draft = this.draft();
      const invalidated = this.invalidatedDraft();
      void this.accountSession();
      void this.savedCover();
      untracked(() => {
        this.finishEditing(false);
        this.previewBroken.set(false);
      });
      if (draft.intent === 'local' && draft !== invalidated) {
        try {
          const url = URL.createObjectURL(draft.blob);
          this.localPreviewUrl.set(url);
          onCleanup(() => URL.revokeObjectURL(url));
        } catch {
          this.localPreviewUrl.set(null);
          this.localError.set('Unable to show the local preview. Choose a smaller image or use an image URL.');
        }
      } else this.localPreviewUrl.set(null);
    });
    let firstAccount = true;
    let account: ReturnType<typeof coverSessionKey> | undefined;
    effect((onCleanup) => {
      const session = this.auth.session();
      const nextAccount = this.accountSession();
      if (!firstAccount && account !== nextAccount) {
        untracked(() => {
          this.searchGeneration++;
          this.clearSearchState();
          this.localError.set('');
          this.finishEditing(false);
          this.invalidatedDraft.set(this.draft());
          this.draftChanged.emit({ intent: 'keep' });
        });
      }
      firstAccount = false;
      account = nextAccount;
      this.uploadsEnabled.set(false);
      this.uploadStatus.set('Checking image upload availability...');
      const request = this.uploads.options().subscribe({
        next: (options) => {
          if (session !== this.auth.session() || this.destroyed) return;
          this.uploadsEnabled.set(options.enabled);
          this.uploadStatus.set(options.enabled ? 'JPEG, PNG or static WebP, up to 20 MiB. Photos stay local until Save. Camera capture depends on your device; Upload image is always the file chooser.' : 'Image uploads are disabled. You can still enter an image URL or search.');
        },
        error: (error: HttpErrorResponse) => {
          if (session !== this.auth.session() || this.destroyed) return;
          this.uploadStatus.set('Image uploads are unavailable. You can still enter an image URL or search.');
          if (error.status === 401) this.unauthorized.emit();
        },
      });
      onCleanup(() => request.unsubscribe());
    });
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      this.searchGeneration++;
      this.preparation.abort();
      this.localGeneration++;
    });
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

  }

  protected async chooseFile(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file || this.disabled() || !this.uploadsEnabled()) return;
    this.finishEditing(false);
    const generation = this.localGeneration;
    const account = this.accountSession();
    const draft = this.draft();
    this.preparing.set(true);
    this.busyChanged.emit(true);
    this.localError.set('');
    try {
      const image = await this.prepareImage(file, this.preparation.signal);
      if (this.destroyed || this.disabled() || generation !== this.localGeneration || account !== this.accountSession() || draft !== this.draft()) return;
      this.preparing.set(false);
      this.editorImage.set(image);
    } catch (error) {
      if (this.destroyed || generation !== this.localGeneration || account !== this.accountSession() || draft !== this.draft()) return;
      this.localError.set(error instanceof Error ? error.message : 'Unable to prepare this image. Try a smaller JPEG or PNG.');
      this.finishEditing();
    }
  }

  protected openChooser(input: HTMLInputElement, trigger: HTMLElement): void {
    if (this.sourceDisabled() || !this.uploadsEnabled()) return;
    this.returnFocus = trigger;
    input.click();
  }

  protected finishEditing(restoreFocus = true): void {
    const wasBusy = this.busy();
    this.localGeneration++;
    this.preparation.abort();
    this.preparation = new AbortController();
    this.preparing.set(false);
    this.editorImage.set(null);
    if (wasBusy && !this.destroyed) this.busyChanged.emit(false);
    if (restoreFocus && this.returnFocus) {
      const target = this.returnFocus;
      afterNextRender(() => { if (!this.destroyed && target.isConnected) target.focus(); }, { injector: this.injector });
    }
  }

  protected useLocalImage(blob: Blob): void {
    if (this.disabled() || !this.editorImage()) return;
    this.selectedCandidate.set(null);
    this.draftChanged.emit({ intent: 'local', blob });
    this.finishEditing();
  }

  protected enterUrl(url: string): void {
    if (this.sourceDisabled()) return;
    this.selectedCandidate.set(null);
    this.localError.set('');
    this.draftChanged.emit(url.trim() ? { intent: 'external', url } : { intent: 'remove' });
  }

  protected search(): void {
    if (this.loading() || this.sourceDisabled()) {
      return;
    }

    const name = this.trimmedName();
    if (name.length === 0) {
      this.error.set('Enter a game name before searching.');
      return;
    }

    const generation = ++this.searchGeneration;
    const account = this.accountSession();
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
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (generation !== this.searchGeneration || account !== this.accountSession()) {
            return;
          }
          const candidates = response.candidates.slice(0, 5);
          this.candidates.set(candidates);
          this.noResults.set(candidates.length === 0);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          if (generation !== this.searchGeneration || account !== this.accountSession()) {
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
    if (this.isPreviewBroken(candidate) || this.sourceDisabled()) {
      return;
    }
    this.selectedCandidate.set(candidate);
    this.localError.set('');
    this.draftChanged.emit({ intent: 'external', url: candidate.imageUrl });
  }

  protected previewUrl(candidate: CoverImageCandidate): string {
    return candidate.thumbnailUrl ?? candidate.imageUrl;
  }

  protected isSelected(candidate: CoverImageCandidate): boolean {
    return this.selectedCandidate()?.imageUrl === candidate.imageUrl && this.manualUrl() === candidate.imageUrl;
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

  protected clearSelectedCover(): void {
    if (this.sourceDisabled()) return;
    this.selectedCandidate.set(null);
    this.localError.set('');
    this.draftChanged.emit({ intent: 'remove' });
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
