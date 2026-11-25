import { HttpClient } from '@angular/common/http';
import { DestroyRef, inject, Injectable } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable, ReplaySubject, Subscription, map } from 'rxjs';

import { environment } from '../../../environments/environment';
import { AuthService } from '../../core/auth/auth.service';
import { PickerRound, RandomPickerFilters, RandomPickerMode, RandomPickerPickResponse } from './random-picker';

interface Work {
  request: (revision: number | undefined) => Observable<PickerRound>;
  result: ReplaySubject<PickerRound>;
}
interface Lane {
  revision?: number;
  work: Work[];
  active?: Subscription;
  running: boolean;
}

// Online, feature-local sequencing only. No browser persistence, offline replay or
// automatic retry. The owning subscription survives page navigation, not logout.
@Injectable({ providedIn: 'root' })
export class PickerRoundService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly lanes = new Map<RandomPickerMode, Lane>();
  private readonly base = `${environment.apiBaseUrl}/random-picker`;

  constructor() {
    const destroy = inject(DestroyRef);
    this.auth.subjectChanges.pipe(takeUntilDestroyed(destroy)).subscribe(() => this.clear());
    destroy.onDestroy(() => this.clear());
  }

  private clear(): void {
    const lanes = [...this.lanes.values()];
    this.lanes.clear();
    for (const lane of lanes) {
      lane.active?.unsubscribe();
      const work = lane.work.splice(0);
      for (const item of work) item.result.error(new Error('Account changed.'));
    }
  }

  load(mode: RandomPickerMode): Observable<PickerRound> {
    return this.enqueue(mode, () => this.http.get<PickerRound>(`${this.base}/rounds/${mode}`));
  }

  saveFilters(mode: RandomPickerMode, filters: RandomPickerFilters): Observable<PickerRound> {
    return this.enqueue(mode, revision => this.http.put<PickerRound>(`${this.base}/rounds/${mode}`,
      { ...filters, mode, ...this.receipt(revision) }));
  }

  pick(mode: RandomPickerMode, filters: RandomPickerFilters): Observable<PickerRound> {
    return this.enqueue(mode, revision => this.http.post<RandomPickerPickResponse>(`${this.base}/pick`,
      { ...filters, mode, ...this.receipt(revision) }).pipe(map(response => {
        if (!response.round) throw new Error('Round acknowledgement missing. Reload before continuing.');
        return response.round;
      })));
  }

  reset(mode: RandomPickerMode): Observable<PickerRound> {
    return this.enqueue(mode, revision => this.http.post<PickerRound>(`${this.base}/rounds/${mode}/reset`, this.receipt(revision)));
  }

  private receipt(revision: number | undefined) {
    if (revision === undefined) throw new Error('Reload this round before continuing.');
    return { roundRevision: revision, operationId: crypto.randomUUID() };
  }

  private enqueue(mode: RandomPickerMode, request: Work['request']): Observable<PickerRound> {
    let lane = this.lanes.get(mode);
    if (!lane) {
      lane = { work: [], running: false };
      this.lanes.set(mode, lane);
    }
    const result = new ReplaySubject<PickerRound>(1);
    lane.work.push({ request, result });
    this.drain(mode, lane);
    return result.asObservable();
  }

  private drain(mode: RandomPickerMode, lane: Lane): void {
    if (lane.running || lane.work.length === 0 || this.lanes.get(mode) !== lane) return;
    const work = lane.work[0];
    lane.running = true;
    try {
      lane.active = work.request(lane.revision).subscribe({
        next: round => {
          lane.revision = round.revision;
          work.result.next(round);
        },
        error: error => this.fail(lane, error),
        complete: () => {
          lane.work.shift();
          lane.running = false;
          work.result.complete();
          this.drain(mode, lane);
        },
      });
    } catch (error) { this.fail(lane, error); }
  }

  private fail(lane: Lane, error: unknown): void {
    lane.revision = undefined;
    lane.running = false;
    const work = lane.work.splice(0);
    for (const item of work) item.result.error(error);
  }
}
