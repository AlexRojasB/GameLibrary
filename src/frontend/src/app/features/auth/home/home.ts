import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { takeUntil } from 'rxjs';

import { environment } from '../../../../environments/environment';

import { AuthService } from '../../../core/auth/auth.service';
import { CurrentlyPlaying, CurrentlyPlayingService } from '../../currently-playing/currently-playing.service';
import { CoverImage } from '../../games/cover-image/cover-image';

type MeState = 'loading' | 'ok' | 'expired' | 'error';

@Component({
  selector: 'app-home',
  imports: [RouterLink, CoverImage],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly selections = inject(CurrentlyPlayingService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly meState = signal<MeState>('loading');
  protected readonly email = computed(() => this.auth.session()?.user.email ?? '');
  protected readonly currentlyPlaying = signal<CurrentlyPlaying>({ videoGame: null, boardGame: null });
  protected readonly playingState = signal<'loading' | 'ok' | 'error'>('loading');
  protected readonly activeSlots = computed(() => [
    { label: 'Video game', item: this.currentlyPlaying().videoGame },
    { label: 'Board game', item: this.currentlyPlaying().boardGame },
  ]);

  constructor() {
    this.auth.subjectChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      this.currentlyPlaying.set({ videoGame: null, boardGame: null });
      this.playingState.set('loading');
      this.meState.set('expired');
      if (this.auth.isAuthenticated()) this.loadProfile();
    });
    this.loadProfile();
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
