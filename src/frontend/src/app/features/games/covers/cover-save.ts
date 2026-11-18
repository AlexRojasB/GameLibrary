import { HttpErrorResponse } from '@angular/common/http';
import { computed, DestroyRef, effect, inject, Injectable, signal } from '@angular/core';
import { Observable, Subscription } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { CoverDraft, CoverReference, CoverUpdate, CoverUploadStatus } from './cover-types';
import { CoverUploadsService } from './cover-uploads.service';
import { coverSessionKey } from './cover-session';

export function coverUpdateFor(draft: CoverDraft, saved: CoverReference | null, uploadId?: string): CoverUpdate {
  const expected = saved === null ? {} : { expectedCover: {
    coverImageUrl: saved.coverImageUrl, coverUploadId: saved.coverUploadId,
  } };
  switch (draft.intent) {
    case 'keep': return { intent: 'keep' };
    case 'remove': return { intent: 'remove', ...expected };
    case 'external': return { intent: 'external', url: draft.url.trim(), ...expected };
    case 'local':
      if (uploadId === undefined) throw new Error('A local cover must be uploaded before saving.');
      return { intent: 'upload', uploadId, ...expected };
  }
}

// Compare only normalized metadata. Cover intent is always an explicit UI action.
export function metadataChanged(input: object, original: object): boolean {
  const previous = original as Record<string, unknown>;
  return Object.entries(input).some(([key, value]) => {
    if (key === 'coverUpdate') return false;
    const normalize = (item: unknown): unknown => typeof item === 'string'
      ? item.trim() || null
      : Array.isArray(item) ? [...item].sort() : item;
    return JSON.stringify(normalize(value)) !== JSON.stringify(normalize(previous[key]));
  });
}

interface SaveJob {
  write: (update: CoverUpdate) => Observable<unknown>;
  refetch: (id: string) => Observable<CoverReference | null>;
  saved: () => void;
  unauthorized: () => void;
  missing: () => void;
  gameId: string | null;
  draft: CoverDraft;
  expected: CoverReference | null;
}

type Phase = 'idle' | 'uploading' | 'saving' | 'unknown' | 'checking' | 'cancelling' | 'retry' | 'expired' | 'conflict';
type SaveSession = ReturnType<typeof coverSessionKey>;

// Provided by each management list, never root: handles/drafts are volatile and account-local.
@Injectable()
export class CoverSave {
  private readonly uploads = inject(CoverUploadsService);
  private readonly auth = inject(AuthService);
  private generation = 0;
  private pending = new Subscription();
  private cancellation = new Subscription();
  private cancellationGeneration = 0;
  private cancellingId: string | null = null;
  private destroyed = false;
  private resolvingConflict = false;
  private ready: CoverUploadStatus | null = null;
  private job: SaveJob | null = null;
  private open = false;
  private owner = this.account();

  readonly draft = signal<CoverDraft>({ intent: 'keep' });
  readonly savedCover = signal<CoverReference | null>(null);
  readonly currentCover = signal<CoverReference | null>(null);
  readonly phase = signal<Phase>('idle');
  readonly message = signal('');
  readonly busy = computed(() => ['uploading', 'saving', 'checking', 'cancelling'].includes(this.phase()));
  readonly blocked = computed(() => this.phase() !== 'idle');
  readonly recovery = computed(() => ['unknown', 'checking', 'retry'].includes(this.phase()));

  constructor() {
    effect(() => {
      const account = this.account();
      if (account !== this.owner) {
        this.owner = account;
        this.invalidateAccount();
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.close();
      this.destroyed = true;
      ++this.cancellationGeneration;
      this.cancellation.unsubscribe();
    });
  }

  account(): SaveSession { return coverSessionKey(this.auth.session()); }

  begin(game: CoverReference | null): boolean {
    if (!this.sessionCurrent() || this.busy() || this.recovery()) return false;
    this.close();
    if (this.busy() || this.recovery()) return false;
    ++this.cancellationGeneration;
    this.open = true;
    this.savedCover.set(game === null ? null : {
      coverImageUrl: game.coverImageUrl, coverUploadId: game.coverUploadId,
    });
    this.message.set('');
    return true;
  }

  setDraft(draft: CoverDraft): void {
    if (!this.sessionCurrent() || this.busy() || this.recovery()) return;
    if (this.mayBeClaimed()) { this.inspection('The upload may already be attached. Check its receipt before changing source.'); return; }
    const old = this.ready;
    this.ready = null;
    this.resolvingConflict = false;
    this.draft.set(draft);
    this.phase.set('idle');
    this.currentCover.set(null);
    this.message.set('');
    if (old !== null) this.cancelUnclaimed(old);
  }

  close(): void {
    if (!this.sessionCurrent()) return;
    const dispatched = this.phase() === 'saving';
    ++this.generation;
    this.pending.unsubscribe();
    this.pending = new Subscription();
    this.open = false;
    if (dispatched || this.mayBeClaimed() || (this.recovery() && !this.resolvingConflict)) {
      this.phase.set('unknown');
      this.message.set('Save outcome unknown. Closing cannot undo a save. Check save status before retrying, or inspect your Library.');
      // Recovery needs the immutable handle and submitted metadata, not the local image.
      if (this.job?.draft.intent === 'local') this.job.draft = { intent: 'keep' };
    } else {
      if (this.cancellingId === null) this.phase.set('idle');
      if (this.ready !== null) this.cancelUnclaimed(this.ready);
      this.ready = null;
      this.job = null;
    }
    this.resolvingConflict = false;
    this.draft.set({ intent: 'keep' });
    this.currentCover.set(null);
  }

  save(job: Omit<SaveJob, 'draft' | 'expected'>): void {
    if (!this.sessionCurrent() || !this.open || this.blocked()) return;
    this.job = { ...job, draft: this.draft(), expected: this.savedCover() };
    this.message.set('');
    if (this.job.draft.intent === 'local' && this.ready === null) {
      this.phase.set('uploading');
      const generation = this.generation;
      const account = this.account();
      this.pending.add(this.uploads.upload(this.job.draft.blob).subscribe({
        next: (status) => {
          if (!this.valid(generation, account) || !this.open) return;
          this.ready = status;
          if (this.mayBeClaimed()) { this.inspection('The upload reports a prior claim. Check its receipt before retrying.'); return; }
          if (!this.live(status)) { this.expired(); return; }
          this.dispatch();
        },
        error: (error: HttpErrorResponse) => {
          if (!this.valid(generation, account)) return;
          this.phase.set('idle');
          this.failure(error, false);
        },
      }));
      return;
    }
    if (this.job.draft.intent === 'local' && !this.live(this.ready)) { this.expired(); return; }
    this.dispatch();
  }

  reupload(): void {
    if (!this.sessionCurrent() || this.phase() !== 'expired' || this.draft().intent !== 'local') return;
    const old = this.ready;
    this.ready = null;
    this.phase.set('idle');
    this.message.set('Ready to upload again. Submit Save when you are ready.');
    if (old !== null) this.cancelUnclaimed(old);
  }

  chooseConflict(replace: boolean): void {
    const current = this.currentCover();
    if (!this.sessionCurrent() || this.phase() !== 'conflict' || current === null) return;
    this.resolvingConflict = false;
    this.savedCover.set({ coverImageUrl: current.coverImageUrl, coverUploadId: current.coverUploadId });
    if (!replace) this.setDraft({ intent: 'keep' });
    if (this.phase() !== 'cancelling') this.phase.set('idle');
    this.currentCover.set(null);
    this.message.set(replace ? 'Current cover reviewed. Save to apply your pending cover and other edits.' : 'Keeping the current cover. Your other edits are unchanged.');
  }

  reconcile(): void {
    if (!this.sessionCurrent() || !this.recovery() || this.busy()) return;
    if (this.ready === null) {
      this.message.set('No upload receipt is available. Save outcome remains unknown. Inspect your Library before making another change.');
      return;
    }
    const generation = this.generation;
    const account = this.account();
    this.phase.set('checking');
    this.pending.add(this.uploads.status(this.ready.id).subscribe({
      next: (status) => {
        if (!this.valid(generation, account)) return;
        this.ready = status;
        if (status.gameId !== null) {
          if (status.gameExists !== true) { this.inspection('The original game was deleted.'); return; }
          this.refetchReceipt(status.gameId, generation, account);
        } else if (this.live(status)) {
          this.phase.set('retry');
          this.message.set('The same upload is Ready and has never been claimed. You may explicitly retry the same save.');
        } else this.inspection('The upload is no longer live; the save outcome cannot be proven.');
      },
      error: (error: HttpErrorResponse) => {
        if (!this.valid(generation, account)) return;
        if (error.status === 401) { this.job?.unauthorized(); return; }
        this.inspection('Recovery evidence is missing, purged or unavailable.');
      },
    }));
  }

  retrySameHandle(): void {
    if (!this.sessionCurrent() || this.phase() !== 'retry' || this.job === null) return;
    if (!this.live(this.ready)) { this.inspection('The recovery handle expired.'); return; }
    this.dispatch(true);
  }

  private dispatch(retry = false): void {
    const job = this.job;
    if (job === null) return;
    const update = retry && this.ready !== null
      ? { intent: 'upload' as const, uploadId: this.ready.id, ...(job.expected === null ? {} : { expectedCover: job.expected }) }
      : coverUpdateFor(job.draft, job.expected, this.ready?.id);
    const generation = this.generation;
    const account = this.account();
    this.phase.set('saving');
    this.pending.add(job.write(update).subscribe({
      next: () => {
        if (!this.valid(generation, account)) return;
        this.ready = null;
        this.phase.set('idle');
        this.job = null;
        this.draft.set({ intent: 'keep' });
        job.saved();
      },
      error: (error: HttpErrorResponse) => {
        if (!this.valid(generation, account)) return;
        if (error.status === 0 || error.status >= 500 || error.error?.code === 'cover_outcome_unknown') {
          this.inspection('The save may have committed. Check save status using the same upload; do not create another game.');
          return;
        }
        this.phase.set('idle');
        this.failure(error, true);
      },
    }));
  }

  private failure(error: HttpErrorResponse, gameWrite: boolean): void {
    if (error.status === 401) { this.job?.unauthorized(); return; }
    if (gameWrite && error.status === 404) { this.job?.missing(); return; }
    if (gameWrite && error.status === 409) {
      this.resolvingConflict = true;
      this.phase.set('unknown');
      if (this.ready !== null) { this.checkConflictHandle(); return; }
      this.fetchConflict();
      return;
    }
    const guidance: Record<number, string> = {
      413: 'Cover image is too large. Choose a smaller image.',
      415: 'Use a JPEG, PNG or static WebP image.',
      422: 'Cover image is invalid. Choose or prepare another image.',
      429: 'Cover limit reached. Wait before retrying; retired uploads may still count toward quota.',
      503: 'Cover uploads are unavailable. Your edits are retained; try later or choose an external URL.',
    };
    this.message.set(error.error?.code === 'cover_intent_required'
      ? 'This app version cannot save covers. Refresh or update the PWA before trying again. Your edits have not been discarded.'
      : typeof error.error?.detail === 'string' ? error.error.detail : guidance[error.status] ?? 'Unable to save. Your edits are retained. Please try again.');
  }

  private checkConflictHandle(): void {
    if (this.ready === null) return;
    const generation = this.generation;
    const account = this.account();
    this.phase.set('checking');
    this.pending.add(this.uploads.status(this.ready.id).subscribe({
      next: (status) => {
        if (!this.valid(generation, account)) return;
        this.ready = status;
        if (status.gameId !== null) {
          if (status.gameExists === true) this.refetchReceipt(status.gameId, generation, account);
          else this.inspection('The original game was deleted.');
        } else if (!this.live(status)) this.expired();
        else this.fetchConflict();
      },
      error: (error: HttpErrorResponse) => {
        if (!this.valid(generation, account)) return;
        if (error.status === 401) this.job?.unauthorized();
        else this.inspection('Upload status is unavailable.');
      },
    }));
  }

  private fetchConflict(): void {
    const job = this.job;
    if (job?.gameId == null) { this.inspection('Create conflicted. Inspect your Library before retrying.'); return; }
    const generation = this.generation;
    const account = this.account();
    this.phase.set('checking');
    this.pending.add(job.refetch(job.gameId).subscribe({
      next: (cover) => {
        if (!this.valid(generation, account)) return;
        if (cover === null) { this.inspection('The game no longer exists.'); return; }
        this.currentCover.set(cover);
        this.phase.set('conflict');
        this.message.set('The cover changed. Review the current cover, then keep it or explicitly apply your pending cover. Other edits are retained.');
      },
      error: (error: HttpErrorResponse) => {
        if (!this.valid(generation, account)) return;
        if (error.status === 401) job.unauthorized();
        else this.inspection('The current cover is unavailable.');
      },
    }));
  }

  private refetchReceipt(id: string, generation: number, account: SaveSession): void {
    this.resolvingConflict = false;
    const job = this.job;
    if (job === null) { this.inspection('Inspect the original game in your Library.'); return; }
    this.pending.add(job.refetch(id).subscribe({
      next: (game) => {
        if (!this.valid(generation, account)) return;
        if (game === null) { this.inspection('The original game is missing or deleted.'); return; }
        this.ready = null;
        this.job = null;
        this.phase.set('idle');
        this.draft.set({ intent: 'keep' });
        this.message.set('The original saved game was recovered from its first-claim receipt. Current fields may have changed since that save.');
        job.saved();
      },
      error: (error: HttpErrorResponse) => {
        if (!this.valid(generation, account)) return;
        if (error.status === 401) job.unauthorized();
        else this.inspection('The original game could not be read.');
      },
    }));
  }

  private cancelUnclaimed(upload: CoverUploadStatus): void {
    if (!this.sessionCurrent() || upload.gameId !== null || upload.state === 'Attached') return;
    const id = upload.id;
    const account = this.account();
    const generation = ++this.cancellationGeneration;
    this.cancellation.unsubscribe();
    this.cancellation = new Subscription();
    const job = this.job;
    if (job?.draft.intent === 'local') job.draft = { intent: 'keep' };
    this.cancellingId = id;
    this.phase.set('cancelling');
    this.cancellation.add(this.uploads.cancel(id).subscribe({
      next: () => {
        if (this.destroyed || generation !== this.cancellationGeneration || this.cancellingId !== id || account !== this.account()) return;
        this.cancellingId = null;
        if (this.phase() === 'cancelling') this.phase.set('idle');
      },
      error: (error: HttpErrorResponse) => {
        if (this.destroyed || generation !== this.cancellationGeneration || this.cancellingId !== id || account !== this.account()) return;
        this.cancellingId = null;
        if (error.status === 401) { job?.unauthorized(); return; }
        if (error.status !== 409) {
          if (this.phase() === 'cancelling') this.phase.set('idle');
          return;
        }
        this.job = job;
        this.ready = { id, state: 'Attached', gameId: null, gameExists: null, expiresAt: null };
        this.inspection('Cancellation conflicted with attachment. Check save status; no attached object was deleted.');
      },
    }));
  }

  private live(status: CoverUploadStatus | null): boolean {
    return status !== null && status.state === 'Ready' && status.gameId === null &&
      status.expiresAt !== null && Date.parse(status.expiresAt) > Date.now();
  }

  private expired(): void {
    this.resolvingConflict = false;
    this.phase.set('expired');
    this.message.set('This staged cover expired or is unavailable. Your local image is retained. Choose Re-upload, then explicitly Save again.');
  }

  private inspection(message: string): void {
    this.resolvingConflict = false;
    this.phase.set('unknown');
    this.message.set(`${message} Save is blocked. Inspect your Library if recovery cannot establish the outcome.`);
  }

  private mayBeClaimed(): boolean {
    return this.ready !== null && (this.ready.gameId !== null || this.ready.state === 'Attached');
  }

  private valid(generation: number, account: SaveSession): boolean {
    return !this.destroyed && generation === this.generation && account === this.account();
  }

  private sessionCurrent(): boolean {
    if (this.destroyed) return false;
    const account = this.account();
    if (account === this.owner) return true;
    this.owner = account;
    this.invalidateAccount();
    return false;
  }

  private invalidateAccount(): void {
    ++this.generation;
    this.pending.unsubscribe();
    this.pending = new Subscription();
    ++this.cancellationGeneration;
    this.cancellation.unsubscribe();
    this.ready = null;
    this.cancellingId = null;
    this.resolvingConflict = false;
    this.job = null;
    this.open = false;
    this.draft.set({ intent: 'keep' });
    this.savedCover.set(null);
    this.currentCover.set(null);
    this.phase.set('idle');
    this.message.set('');
  }
}
