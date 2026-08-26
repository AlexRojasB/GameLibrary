import { Component, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';

import {
  SteamService,
  SteamStatusResponse,
  SteamPreviewCandidate,
  SteamLibraryPreviewResponse,
  SteamImportResponse,
} from './steam.service';

@Component({
  selector: 'app-steam-page',
  templateUrl: './steam-page.html',
  styleUrl: './steam-page.scss',
})
export class SteamPage implements OnInit {
  private readonly steam = inject(SteamService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly status = signal<SteamStatusResponse | null>(null);
  protected readonly loadingStatus = signal(true);
  protected readonly linking = signal(false);
  protected readonly unlinking = signal(false);
  protected readonly previewing = signal(false);
  protected readonly importing = signal(false);
  protected readonly candidates = signal<SteamPreviewCandidate[]>([]);
  protected readonly selected = signal<Set<number>>(new Set());
  protected readonly previewState = signal<string | null>(null);
  protected readonly previewMessage = signal<string | null>(null);
  protected readonly importResult = signal<SteamImportResponse | null>(null);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly hasSelectedSelectable = signal(false);

  ngOnInit(): void {
    const queryParams = this.route.snapshot.queryParams;
    if (queryParams['steamLink'] === 'success') {
      this.router.navigate([], { queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true });
    } else if (queryParams['steamLink'] === 'failure') {
      this.router.navigate([], { queryParams: { steamLink: null }, queryParamsHandling: 'merge', replaceUrl: true });
    }

    this.loadStatus();
  }

  private loadStatus(): void {
    this.loadingStatus.set(true);
    this.steam.getStatus().subscribe({
      next: (status) => {
        this.status.set(status);
        this.loadingStatus.set(false);
      },
      error: () => {
        this.errorMessage.set('Unable to load Steam status.');
        this.loadingStatus.set(false);
      },
    });
  }

  protected onLink(): void {
    this.linking.set(true);
    this.errorMessage.set(null);
    this.steam.createLinkRequest().subscribe({
      next: (response) => {
        window.location.href = response.startUrl;
      },
      error: (err) => {
        if (err?.status === 409) {
          this.errorMessage.set('Unlink the current Steam account before linking another account.');
        } else {
          this.errorMessage.set('Unable to start Steam link flow.');
        }
        this.linking.set(false);
      },
    });
  }

  protected onUnlink(): void {
    this.unlinking.set(true);
    this.errorMessage.set(null);
    this.steam.unlink().subscribe({
      next: () => {
        this.candidates.set([]);
        this.selected.set(new Set());
        this.previewState.set(null);
        this.importResult.set(null);
        this.loadStatus();
        this.unlinking.set(false);
      },
      error: () => {
        this.errorMessage.set('Unable to unlink Steam account.');
        this.unlinking.set(false);
      },
    });
  }

  protected onPreview(): void {
    this.previewing.set(true);
    this.errorMessage.set(null);
    this.importResult.set(null);
    this.selected.set(new Set());
    this.steam.preview().subscribe({
      next: (response) => {
        this.candidates.set(response.candidates);
        this.previewState.set(response.state);
        this.previewMessage.set(response.message);
        this.previewing.set(false);
      },
      error: (err) => {
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
    this.selected.update((set) => {
      const next = new Set(set);
      if (next.has(steamAppId)) {
        next.delete(steamAppId);
      } else {
        next.add(steamAppId);
      }
      return next;
    });
    this.hasSelectedSelectable.set(this.getSelectableSelected().length > 0);
  }

  protected onImport(): void {
    const appIds = this.getSelectableSelected();
    if (appIds.length === 0) return;

    this.importing.set(true);
    this.errorMessage.set(null);
    this.steam.import(appIds).subscribe({
      next: (response) => {
        this.importResult.set(response);
        this.importing.set(false);
        this.selected.set(new Set());
        this.loadStatus();
      },
      error: (err) => {
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
