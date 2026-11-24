import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { AuthService } from '../../../core/auth/auth.service';
import { SteamLinkBrowser, SteamLinkHandle, trustedSteamUrl, validSteamId } from './steam-link-browser';
import { SteamLinkReview, SteamService } from './steam.service';

@Injectable({ providedIn: 'root' })
export class SteamLinkFlow {
  private readonly auth = inject(AuthService);
  private readonly steam = inject(SteamService);
  private readonly browser = inject(SteamLinkBrowser);
  private readonly destroyRef = inject(DestroyRef);
  private timer?: ReturnType<typeof setTimeout>;
  private operation = 0;
  readonly handle = signal<SteamLinkHandle | null>(null);
  readonly review = signal<SteamLinkReview | null>(null);
  readonly busy = signal(false);
  readonly message = signal<string | null>(null);
  readonly revision = signal(0);

  constructor() {
    this.auth.subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.operation++;
      this.forget();
      this.busy.set(false);
      this.message.set('Account changed; start again.');
    });
    this.destroyRef.onDestroy(() => clearTimeout(this.timer));
    void this.auth.initialize().then(() => {
      try {
        const stored = this.browser.read();
        if (stored && (stored.initiatingUserId !== this.auth.session()?.user.id || Date.parse(stored.expiresAt) <= Date.now())) this.forget();
      } catch { this.forget(); }
    });
  }

  restore(expectHandle = false): void {
    this.review.set(null); // Reload/return never reuses a previous consent presentation.
    try {
      const handle = this.browser.read();
      if (handle && handle.initiatingUserId !== this.auth.session()?.user.id) {
        this.forget(); this.message.set('Account changed; start again.'); return;
      }
      this.handle.set(handle);
      if (!handle && expectHandle) this.message.set('This tab has no live linking request. Start again in this browser.');
      if (handle && !this.live()) return;
      if (handle) this.armExpiry();
    } catch { this.forget(); this.message.set('Tab storage is unavailable. Start again in a supported browser.'); }
  }

  start(): void {
    const user = this.auth.session()?.user.id;
    if (!user || this.busy()) return;
    const current = this.begin();
    this.steam.createLinkRequest().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (created) => {
        if (!current()) return;
        // Never retain the full response/authorization URL in signals or storage.
        if (!created || created.protocolVersion !== 2 || !trustedSteamUrl(created.authorizationUrl)) {
          this.forget(); this.busy.set(false); this.message.set('Steam destination could not be verified. Start again.'); return;
        }
        const handle = { transactionId: created.transactionId, initiatingUserId: user, expiresAt: created.expiresAt };
        try {
          this.browser.save(handle);
          this.handle.set(handle);
          if (!this.live()) { this.busy.set(false); return; }
          this.armExpiry();
          this.browser.navigate(created.authorizationUrl);
          // Remain busy until native departure, avoiding a second creation in this tab.
        } catch {
          this.forget(); this.busy.set(false);
          this.message.set('Unable to continue securely. Check tab storage and start again.');
        }
      },
      error: (error) => { if (current()) { this.forget(); this.failure(error, 'Creation outcome is unknown. Explicitly start again; do not reuse the old Steam URL.'); } },
    });
  }

  refresh(): void {
    if (this.busy() || !this.live()) return;
    const handle = this.handle()!;
    const current = this.begin();
    this.steam.reviewLink(handle.transactionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (review) => {
        if (!current()) return;
        this.busy.set(false);
        if (!this.live()) return;
        if (!review || review.expiresAt !== handle.expiresAt || !['Pending', 'AwaitingConfirmation', 'Linked'].includes(review.state)
            || (review.state !== 'Pending' && !validSteamId(review.steamId64)) || (review.state === 'Pending' && review.steamId64 !== null)) {
          this.forget(); this.message.set('Steam review could not be verified. Start again.'); return;
        }
        if (review.state === 'Linked') { this.complete('Steam account linked.'); return; }
        this.review.set(review);
        if (review.state === 'Pending') this.message.set('Steam verification has not completed. You may check again explicitly or cancel and restart.');
      },
      error: (error) => { if (current()) this.failure(error, 'Unable to establish the linking outcome. Check again explicitly before confirming.'); },
    });
  }

  confirm(): void {
    const reviewed = this.review();
    if (this.busy() || !this.live() || reviewed?.state !== 'AwaitingConfirmation' || !validSteamId(reviewed.steamId64)) return;
    const expected = reviewed.steamId64;
    const current = this.begin(); // Immediately hide consent; failure requires fresh durable review.
    this.steam.confirmLink(this.handle()!.transactionId, expected).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (result) => {
        if (!current()) return;
        this.busy.set(false);
        if (result?.state === 'Linked' && result.steamId64 === expected) this.complete('Steam account linked.');
        else this.message.set('Confirmation outcome is unknown. Check linking status before trying again.');
      },
      error: (error) => { if (current()) this.failure(error, 'Confirmation outcome is unknown. Check linking status before trying again.'); },
    });
  }

  cancel(): void {
    if (this.busy() || !this.live()) return;
    const current = this.begin();
    this.steam.cancelLink(this.handle()!.transactionId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { if (current()) { this.busy.set(false); this.complete('Linking cancelled. Any already completed link is unchanged.'); } },
      error: (error) => { if (current()) this.failure(error, 'Cancellation outcome is unknown. Check linking status; cancellation never unlinks an account.'); },
    });
  }

  forget(): void {
    clearTimeout(this.timer);
    this.handle.set(null); this.review.set(null);
    try { this.browser.clear(); } catch { /* Unusable storage never grants authority. */ }
  }

  suspend(): void { this.operation++; this.busy.set(false); this.review.set(null); }

  private live(): boolean {
    const handle = this.handle();
    if (!handle) { if (!this.message()) this.message.set('This tab has no live linking request. Start again.'); return false; }
    if (handle.initiatingUserId !== this.auth.session()?.user.id) {
      this.forget(); this.message.set('Account changed; start again.'); return false;
    }
    if (Date.parse(handle.expiresAt) <= Date.now()) {
      this.forget(); this.message.set('The linking request expired. Start again.'); return false;
    }
    return true; // UX only. PostgreSQL and JWT remain authoritative.
  }
  private armExpiry(): void {
    clearTimeout(this.timer);
    this.timer = setTimeout(() => { this.operation++; this.busy.set(false); this.live(); }, Math.max(0, Date.parse(this.handle()!.expiresAt) - Date.now()));
  }
  private begin(): () => boolean {
    const operation = ++this.operation; const generation = this.auth.generation(); const user = this.auth.session()?.user.id;
    this.busy.set(true); this.message.set(null); this.review.set(null);
    return () => operation === this.operation && generation === this.auth.generation() && !!user && user === this.auth.session()?.user.id;
  }
  private complete(message: string): void {
    this.forget(); this.message.set(message); this.revision.update((value) => value + 1);
  }
  private failure(error: { status?: number; error?: { code?: string } }, unknown: string): void {
    this.busy.set(false); this.review.set(null);
    if (error.status === 404 || ['LINK_REQUEST_NOT_ACTIVE', 'LINK_NOT_CURRENT'].includes(error.error?.code ?? '')) {
      this.forget(); this.message.set('This request is expired, replaced or missing browser proof. Start again in the original browser.');
    } else if (error.status === 401) this.message.set('Sign in again, then check linking status and review before confirming.');
    else if (error.error?.code === 'LINK_CONFLICT') this.message.set('Steam account could not be linked. Cancel this request and start again.');
    else if (error.status === 429) this.message.set('Too many requests. Wait before explicitly checking again.');
    else if (error.error?.code === 'REVIEW_REQUIRED') this.message.set('Review is required. Check linking status again.');
    else this.message.set(unknown);
  }
}
