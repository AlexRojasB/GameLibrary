import { Component, computed, DestroyRef, effect, HostListener, inject, OnDestroy, OnInit, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { SteamLinkFlow } from './steam-link-flow';

import {
  SteamService,
  SteamStatusResponse,
  SteamPreviewCandidate,
  SteamImportResponse,
} from './steam.service';

@Component({
  selector: 'app-steam-page',
  templateUrl: './steam-page.html',
  styleUrl: './steam-page.scss',
})
export class SteamPage implements OnInit, OnDestroy {
  protected readonly maxImportGames = 100;
  private readonly steam = inject(SteamService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly linking = inject(SteamLinkFlow);
  protected readonly accountLabel = computed(() => this.auth.session()?.user.email || this.auth.session()?.user.id || 'Signed out');
  protected readonly canLink = computed(() => this.status()?.enabled === true && this.status()?.linkAvailable === true && this.status()?.linkProtocolVersion === 2);
  private initialized = false;
  private resumeRequested = false;

  constructor() {
    this.auth.subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.status.set(null); this.clearPrivateResults(); this.errorMessage.set(null);
      this.unlinking.set(false); this.previewing.set(false); this.importing.set(false);
      if (this.initialized && this.auth.isAuthenticated()) this.loadStatus();
    });
    effect(() => {
      if (this.linking.revision() > 0) untracked(() => { if (this.initialized) this.loadStatus(); });
    });
  }

  protected readonly status = signal<SteamStatusResponse | null>(null);
  protected readonly loadingStatus = signal(true);
  protected readonly unlinking = signal(false);
  protected readonly previewing = signal(false);
  protected readonly importing = signal(false);
  protected readonly candidates = signal<SteamPreviewCandidate[]>([]);
  protected readonly selected = signal<Set<number>>(new Set());
  protected readonly previewState = signal<string | null>(null);
  protected readonly previewMessage = signal<string | null>(null);
  protected readonly importResult = signal<SteamImportResponse | null>(null);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly callbackNotice = signal<string | null>(null);
  protected readonly callbackFailed = signal(false);
  protected readonly selectedCount = computed(() => this.getSelectableSelected().length);
  protected readonly selectableCount = computed(() => this.candidates().filter((c) => !c.alreadyImported).length);

  protected readonly hasSelectedSelectable = computed(() => this.selectedCount() > 0 && this.selectedCount() <= this.maxImportGames);
  protected readonly allSelectableSelected = computed(() => {
    const target = Math.min(this.selectableCount(), this.maxImportGames);
    return target > 0 && this.selectedCount() === target;
  });

  ngOnInit(): void {
    this.initialized = true;
    const queryParams = this.route.snapshot.queryParams;
    this.resumeRequested = queryParams['steamLink'] === 'review' || queryParams['steamLink'] === 'failure';
    if (queryParams['steamLink'] === 'success') {
      this.callbackNotice.set('Steam linking is temporarily unavailable. Check your existing linked account below.');
      this.router.navigate([], { queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true });
    } else if (queryParams['steamLink'] === 'review') {
      this.callbackNotice.set('Review the verified Steam identity before choosing whether to link it.');
      this.router.navigate([], { queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true });
    } else if (queryParams['steamLink'] === 'failure') {
      this.callbackFailed.set(true);
      this.callbackNotice.set('Steam did not complete the return. Check linking status or cancel and start again.');
      this.router.navigate([], { queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }

    this.loadStatus();
  }

  ngOnDestroy(): void { this.initialized = false; this.linking.suspend(); }

  @HostListener('window:pageshow', ['$event'])
  protected onPageShow(event: PageTransitionEvent): void {
    if (event.persisted) { this.linking.suspend(); this.loadStatus(); }
  }

  @HostListener('document:visibilitychange')
  protected onVisibilityChange(): void {
    if (document.visibilityState === 'hidden') this.linking.review.set(null);
  }

  protected onLink(): void {
    if (!this.canLink() || this.status()?.linked || this.unlinking() || this.previewing() || this.importing()) return;
    this.linking.start();
  }

  private clearPrivateResults(): void {
    this.candidates.set([]); this.selected.set(new Set()); this.previewState.set(null);
    this.previewMessage.set(null); this.importResult.set(null);
  }

  private current(): () => boolean {
    const generation = this.auth.generation();
    return () => this.initialized && generation === this.auth.generation();
  }

  private loadStatus(): void {
    const current = this.current();
    this.loadingStatus.set(true);
    this.steam.getStatus().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (status) => {
        if (!current()) return;
        this.status.set(status);
        this.loadingStatus.set(false);
        if (this.canLink()) {
          void this.auth.initialize().then(() => {
            if (!current()) return;
            this.linking.restore(this.resumeRequested);
            this.resumeRequested = false;
            if (this.linking.handle()) this.linking.refresh();
          });
        } else this.linking.suspend();
      },
      error: () => {
        if (!current()) return;
        this.errorMessage.set('Unable to load Steam status.');
        this.loadingStatus.set(false);
      },
    });
  }

  protected onUnlink(): void {
    if (this.unlinking() || this.previewing() || this.importing() || this.linking.busy()) return;

    this.unlinking.set(true);
    this.errorMessage.set(null);
    const current = this.current();
    this.steam.unlink().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        if (!current()) return;
        this.linking.forget();
        this.candidates.set([]);
        this.selected.set(new Set());
        this.previewState.set(null);
        this.previewMessage.set(null);
        this.importResult.set(null);
        this.loadStatus();
        this.unlinking.set(false);
      },
      error: () => {
        if (!current()) return;
        this.errorMessage.set('Unable to unlink Steam account.');
        this.unlinking.set(false);
      },
    });
  }

  protected onPreview(): void {
    if (this.previewing() || this.importing() || this.unlinking()) return;

    this.previewing.set(true);
    this.errorMessage.set(null);
    this.importResult.set(null);
    this.selected.set(new Set());
    const current = this.current();
    this.steam.preview().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (!current()) return;
        this.candidates.set(response.candidates);
        this.previewState.set(response.state);
        this.previewMessage.set(response.message);
        this.previewing.set(false);
      },
      error: (err) => {
        if (!current()) return;
        if (err?.status === 404) {
          this.errorMessage.set('Link a Steam account before importing Steam games.');
        } else if (err?.status === 503) {
          this.errorMessage.set(err.error?.detail || 'Steam integration is unavailable.');
        } else {
          this.errorMessage.set('Unable to preview Steam library.');
        }
        this.previewing.set(false);
      },
    });
  }

  protected onToggle(steamAppId: number): void {
    if (this.previewing() || this.importing() || this.unlinking()
      || !this.candidates().some((c) => c.steamAppId === steamAppId && !c.alreadyImported)) return;
    if (!this.selected().has(steamAppId) && this.selectedCount() >= this.maxImportGames) return;

    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(steamAppId)) {
        next.delete(steamAppId);
      } else {
        next.add(steamAppId);
      }
      return next;
    });
  }

  protected onToggleAll(): void {
    if (this.previewing() || this.importing() || this.unlinking()) return;

    const selectable = this.candidates().filter((c) => !c.alreadyImported);

    if (this.allSelectableSelected()) {
      this.selected.set(new Set());
    } else {
      const next = new Set(this.getSelectableSelected());
      for (const candidate of selectable) {
        if (next.size >= this.maxImportGames) break;
        next.add(candidate.steamAppId);
      }
      this.selected.set(next);
    }
  }

  protected onImport(): void {
    if (this.importing() || this.previewing() || this.unlinking()) return;

    const appIds = this.getSelectableSelected();
    if (appIds.length === 0 || appIds.length > this.maxImportGames) return;

    this.importing.set(true);
    this.errorMessage.set(null);
    const current = this.current();
    this.steam.import(appIds).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (response) => {
        if (!current()) return;
        this.importResult.set(response);
        const importedIds = new Set([
          ...response.imported.map((game) => game.steamAppId),
          ...response.alreadyImported,
        ]);
        this.candidates.update((candidates) => candidates.map((candidate) =>
          importedIds.has(candidate.steamAppId) ? { ...candidate, alreadyImported: true } : candidate));
        this.importing.set(false);
        this.selected.set(new Set());
        this.loadStatus();
      },
      error: (err) => {
        if (!current()) return;
        if (err?.status === 404) {
          this.errorMessage.set('Link a Steam account before importing Steam games.');
        } else if (err?.status === 500) {
          this.errorMessage.set('Steam import failed.');
        } else {
          this.errorMessage.set(err.error?.detail || 'Unable to import Steam games.');
        }
        this.importing.set(false);
      },
    });
  }

  private getSelectableSelected(): number[] {
    return this.candidates()
      .filter((c) => !c.alreadyImported && this.selected().has(c.steamAppId))
      .map((c) => c.steamAppId);
  }
}
