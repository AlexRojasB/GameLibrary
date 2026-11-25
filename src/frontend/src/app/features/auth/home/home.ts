import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { takeUntil } from 'rxjs';

import { environment } from '../../../../environments/environment';

import { AuthService } from '../../../core/auth/auth.service';
import { CurrentlyPlaying, CurrentlyPlayingItem, CurrentlyPlayingService } from '../../currently-playing/currently-playing.service';
import { CoverImage } from '../../games/cover-image/cover-image';
import { CreatePlayLogEntryRequest } from '../../play-log/play-log';
import { LogPlayDialog } from '../../play-log/log-play-dialog/log-play-dialog';
import { PlayLogService } from '../../play-log/play-log.service';

type MeState = 'loading' | 'ok' | 'expired' | 'error';

@Component({
  selector: 'app-home',
  imports: [RouterLink, CoverImage, LogPlayDialog],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly logPlayDialog = viewChild<LogPlayDialog>('logPlayDialog');
  private readonly playLog = inject(PlayLogService);
  private loggingGameId: string | null = null;
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly selections = inject(CurrentlyPlayingService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly meState = signal<MeState>('loading');
  protected readonly email = computed(() => this.auth.session()?.user.email ?? '');
  protected readonly currentlyPlaying = signal<CurrentlyPlaying>({ videoGame: null, boardGame: null });
  protected readonly playingState = signal<'loading' | 'ok' | 'error'>('loading');
  protected readonly logPendingGameId = signal<string | null>(null);
  protected readonly logSuccess = signal('');
  protected readonly activeSlots = computed(() => [
    { label: 'Video game', item: this.currentlyPlaying().videoGame },
    { label: 'Board game', item: this.currentlyPlaying().boardGame },
  ]);

  constructor() {
    this.auth.subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.logPlayDialog()?.close();
      this.loggingGameId = null;
      this.logPendingGameId.set(null);
      this.logSuccess.set('');
      this.currentlyPlaying.set({ videoGame: null, boardGame: null });
      this.playingState.set('loading');
      this.meState.set('expired');
      if (this.auth.isAuthenticated()) this.loadProfile();
    });
    this.loadProfile();
  }

  protected logPlayTime(item: CurrentlyPlayingItem): void {
    if (this.logPendingGameId() !== null || this.meState() !== 'ok' || this.playingState() !== 'ok'
      || !this.activeSlots().some(slot => slot.item?.gameId === item.gameId)) return;
    this.loggingGameId = item.gameId;
    this.logSuccess.set('');
    this.logPlayDialog()?.open({ id: item.gameId, name: item.name });
  }

  protected onLogCancelled(): void {
    this.loggingGameId = null;
  }

  protected onLogPlayConfirmed(request: CreatePlayLogEntryRequest): void {
    if (this.logPendingGameId() !== null || this.loggingGameId !== request.gameId) return;
    this.logPendingGameId.set(request.gameId);
    this.logPlayDialog()?.setPending(true);
    this.playLog.logPlay(request)
      .pipe(takeUntilDestroyed(this.destroyRef), takeUntil(this.auth.subjectChanges)).subscribe({
        next: () => {
          this.logPendingGameId.set(null);
          this.loggingGameId = null;
          this.logPlayDialog()?.close();
          this.logSuccess.set('Play logged.');
          this.loadCurrentlyPlaying();
        },
        error: (error: HttpErrorResponse) => {
          this.logPendingGameId.set(null);
          if (error.status === 401) {
            this.auth.clearLocalSession();
            return;
          }
          const detail = (error.error as { detail?: unknown } | null)?.detail;
          this.logPlayDialog()?.showError(typeof detail === 'string' && detail.trim()
            ? detail : 'Unable to log this play. Please try again.');
        },
      });
  }

  private loadProfile(): void {
    this.http.get<{ userId: string }>(`${environment.apiBaseUrl}/auth/me`)
      .pipe(takeUntilDestroyed(this.destroyRef), takeUntil(this.auth.subjectChanges)).subscribe({
        next: () => {
          this.meState.set('ok');
          this.loadCurrentlyPlaying();
        },
        error: (error: HttpErrorResponse) => {
          if (error.status === 401) {
            this.auth.clearLocalSession();
            this.meState.set('expired');
          } else {
            this.meState.set('error');
          }
        },
      });
  }

  protected loadCurrentlyPlaying(): void {
    this.playingState.set('loading');
    this.selections.get().pipe(takeUntilDestroyed(this.destroyRef), takeUntil(this.auth.subjectChanges)).subscribe({
      next: (slots) => {
        this.currentlyPlaying.set(slots);
        this.playingState.set('ok');
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.auth.clearLocalSession();
          this.meState.set('expired');
          this.currentlyPlaying.set({ videoGame: null, boardGame: null });
        } else {
          this.playingState.set('error');
        }
      },
    });
  }
}
