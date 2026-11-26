import { Location } from '@angular/common';
import { inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationCancel, NavigationEnd, NavigationError, NavigationSkipped, NavigationStart, Router } from '@angular/router';

import { AuthService } from '../auth/auth.service';

const destinations = {
  '/': 'Home',
  '/library': 'Library',
  '/random-picker': 'Pick',
  '/manage': 'Manage',
  '/video-games': 'Video games',
  '/board-games': 'Board games',
  '/platforms': 'Platforms',
  '/play-log': 'Play Log',
  '/health': 'Health',
} as const;

type Destination = keyof typeof destinations;
export type BackFallback = '/' | '/manage';
interface Entry { id: number; path: Destination }

@Injectable({ providedIn: 'root' })
export class BackNavigation {
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly history = signal<{ entries: Entry[]; index: number }>({ entries: [], index: -1 });
  private start: { restoredId: number | null; traversal: boolean; safeRequest: boolean } | null = null;
  private readonly navigating = signal(false);
  readonly busy = this.navigating.asReadonly();

  constructor() {
    inject(AuthService).subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => this.clear());
    this.router.events.pipe(takeUntilDestroyed()).subscribe(event => {
      if (event instanceof NavigationStart) {
        if (event.navigationTrigger === 'imperative' && !this.currentEntryMatches()) this.clear();
        this.navigating.set(true);
        this.start = {
          restoredId: event.navigationTrigger === 'popstate' ? event.restoredState?.navigationId ?? null : null,
          traversal: event.navigationTrigger !== 'imperative',
          safeRequest: isDestination(event.url),
        };
      } else if (event instanceof NavigationEnd) {
        this.record(event);
        this.start = null;
        this.navigating.set(false);
      } else if (event instanceof NavigationCancel || event instanceof NavigationError || event instanceof NavigationSkipped) {
        // With the Router's default cancellation recovery, a failed traversal
        // may replace the visited slot rather than restore the old cursor.
        // Never assume offsets still match after it (including skipped popstate).
        const trigger = this.router.currentNavigation()?.trigger;
        if (this.start?.traversal || trigger === 'popstate' || trigger === 'hashchange') this.clear();
        this.start = null;
        this.navigating.set(false);
      }
    });
  }

  label(fallback: BackFallback): string {
    return `Back to ${destinations[this.previous()?.entry.path ?? parent(fallback)]}`;
  }

  back(fallback: BackFallback): void {
    if (this.busy() || this.router.currentNavigation()) return;
    this.navigating.set(true);
    const previous = this.previous();
    if (previous) {
      // Only traverse entries observed in this boot, never an external/referrer
      // entry or a saved auth callback. This retains the browser Forward chain.
      this.location.historyGo(previous.offset);
    } else {
      void this.router.navigateByUrl(parent(fallback), { replaceUrl: true });
    }
  }

  private previous(): { entry: Entry; offset: number } | null {
    const { entries, index } = this.history();
    const current = entries[index];
    if (!current || !this.currentEntryMatches()) return null;
    for (let i = index - 1; i >= 0; i--) {
      if (entries[i].path !== current.path) return { entry: entries[i], offset: i - index };
    }
    return null;
  }

  private currentEntryMatches(): boolean {
    const { entries, index } = this.history();
    const current = entries[index];
    const state = this.location.getState() as { navigationId?: number } | null;
    return !!current && state?.navigationId === current.id
      && (this.location.path(true) || '/') === current.path && this.router.url === current.path;
  }

  private record(event: NavigationEnd): void {
    const navigation = this.router.currentNavigation();
    if (navigation?.extras.skipLocationChange || !isDestination(event.urlAfterRedirects)) {
      this.clear();
      return;
    }
    if (!this.start?.safeRequest) this.clear();
    const { entries, index } = this.history();
    const entry: Entry = { id: event.id, path: event.urlAfterRedirects };
    const restoredIndex = entries.findIndex(item => item.id === this.start?.restoredId);
    if (restoredIndex >= 0) {
      const next = [...entries];
      next[restoredIndex] = entry;
      this.history.set({ entries: next, index: restoredIndex });
    } else if (this.start?.traversal) {
      // A traversal from before boot/reload or an invalidated account boundary.
      this.history.set({ entries: [entry], index: 0 });
    } else if (index >= 0 && (navigation?.extras.replaceUrl || entries[index].path === entry.path)) {
      const next = [...entries];
      next[index] = entry;
      this.history.set({ entries: next, index });
    } else {
      const next = [...entries.slice(0, index + 1), entry];
      this.history.set({ entries: next, index: next.length - 1 });
    }
  }

  private clear(): void {
    this.history.set({ entries: [], index: -1 });
  }
}

function isDestination(url: string): url is Destination {
  return Object.hasOwn(destinations, url);
}

function parent(fallback: BackFallback): BackFallback {
  return fallback === '/manage' ? '/manage' : '/';
}
