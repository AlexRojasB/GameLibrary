import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, ElementRef, inject, signal, untracked, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';

import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';

import { Genre } from '../genres';
import { GenresService } from '../genres.service';
import { VideoGame, VideoGameInput } from '../video-game';
import { VideoGamesService } from '../video-games.service';
import { VideoGameForm } from '../video-game-form/video-game-form';
import { CoverSave, metadataChanged } from '../covers/cover-save';
import { CoverUploadsService } from '../covers/cover-uploads.service';
import { CoverImage } from '../cover-image/cover-image';

@Component({
  selector: 'app-video-games-list',
  imports: [RouterLink, VideoGameForm, CoverImage],
  providers: [CoverSave],
  templateUrl: './video-games-list.html',
  styleUrl: './video-games-list.scss',
})
export class VideoGamesList {
  private readonly formDialog = viewChild<ElementRef<HTMLDialogElement>>('formDialog');
  private readonly videoGamesService = inject(VideoGamesService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly coverSave = inject(CoverSave);
  private readonly coverUploads = inject(CoverUploadsService);
  private loadGeneration = 0;

  protected readonly loading = signal(true);
  protected readonly search = signal('');
  protected readonly videoGames = signal<VideoGame[]>([]);
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly loadError = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingGame = signal<VideoGame | null>(null);
  protected readonly notice = signal('');
  private returnFocusTo: HTMLElement | null = null;

  constructor() {
    let account = this.coverSave.account();
    effect(() => {
      const current = this.coverSave.account();
      if (current !== account) {
        account = current;
        untracked(() => {
          ++this.loadGeneration;
          this.closeForm();
          this.videoGames.set([]);
          this.search.set('');
          this.platforms.set([]);
          this.editingGame.set(null);
          this.notice.set('');
          if (current !== null) {
            this.loadPlatforms();
            this.load();
          }
        });
      }
    });
    this.loadPlatforms();
    this.loadGenres();
    this.load();
  }

  load(): void {
    const generation = ++this.loadGeneration;
    const account = this.coverSave.account();
    this.loading.set(true);
    this.loadError.set(false);
    this.videoGamesService.list(this.search()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (games) => {
        if (generation !== this.loadGeneration || account !== this.coverSave.account()) return;
        this.videoGames.set(games);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        if (generation !== this.loadGeneration || account !== this.coverSave.account()) return;
        this.loading.set(false);
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.loadError.set(true);
      },
    });
  }

  protected onSearch(value: string): void {
    const previous = this.search().trim();
    this.search.set(value);
    if (value.trim() !== previous) this.load();
  }

  protected clearSearch(input: HTMLInputElement): void {
    this.onSearch('');
    input.focus();
  }

  openCreate(): void {
    if (!this.coverSave.begin(null)) return;
    this.editingGame.set(null);
    this.openForm();
  }

  openEdit(game: VideoGame): void {
    if (!this.coverSave.begin(game)) return;
    this.editingGame.set(game);
    this.openForm();
  }

  closeForm(): void {
    this.coverSave.close();
    const dialog = this.formDialog()?.nativeElement;
    if (dialog?.open) {
      if (typeof dialog.close === 'function') {
        dialog.close();
      } else {
        dialog.removeAttribute('open');
      }
    }
    this.formOpen.set(false);
    this.returnFocusTo?.focus();
    this.returnFocusTo = null;
  }

  protected onDialogCancel(event: Event): void {
    event.preventDefault();
    this.closeForm();
  }

  protected onDialogBackdropClick(event: MouseEvent): void {
    if (event.target === this.formDialog()?.nativeElement) {
      this.closeForm();
    }
  }

  onSubmit(input: VideoGameInput): void {
    const editing = this.editingGame();
    const coverOnly = editing !== null && !metadataChanged(input, editing);
    this.coverSave.save({
      gameId: editing?.id ?? null,
      write: (coverUpdate) => coverOnly && coverUpdate.intent !== 'keep'
        ? this.coverUploads.update(editing!.id, coverUpdate)
        : editing === null ? this.videoGamesService.create({ ...input, coverUpdate })
          : this.videoGamesService.update(editing.id, { ...input, coverUpdate }),
      refetch: (id) => this.videoGamesService.list().pipe(map((games) => games.find((game) => game.id === id) ?? null)),
      saved: () => { this.closeForm(); this.load(); },
      unauthorized: () => this.handleSessionExpired(),
      missing: () => { this.notice.set('This video game no longer exists.'); this.closeForm(); this.load(); },
    });
  }

  onDelete(game: VideoGame): void {
    if (this.coverSave.busy() || this.coverSave.recovery()) return;
    const account = this.coverSave.account();
    const confirmed = window.confirm(`Delete "${game.name}"?`);
    if (!confirmed) {
      return;
    }
    this.videoGamesService.delete(game.id).subscribe({
      next: () => { if (account === this.coverSave.account()) this.load(); },
      error: (error: HttpErrorResponse) => {
        if (account !== this.coverSave.account()) return;
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        if (error.status === 404) {
          this.notice.set('This video game no longer exists.');
          this.load();
          return;
        }
        this.notice.set('Unable to delete the video game. Please try again.');
      },
    });
  }

  protected platformName(id: string): string {
    return this.platforms().find((platform) => platform.id === id)?.name ?? 'Unknown platform';
  }

  protected platformNames(game: VideoGame): string {
    return game.platformIds.map((id) => this.platformName(id)).join(', ');
  }

  protected genreNames(game: VideoGame): string {
    return game.genreIds.map((id) => this.genreName(id)).join(', ');
  }

  protected playerRange(game: VideoGame): string {
    if (game.minimumPlayers === null || game.maximumPlayers === null) {
      return '';
    }
    if (game.minimumPlayers === 1 && game.maximumPlayers === 1) {
      return '1 player';
    }
    return `${game.minimumPlayers}-${game.maximumPlayers} players`;
  }

  private genreName(id: string): string {
    return this.genres().find((genre) => genre.id === id)?.name ?? 'Unknown genre';
  }

  private openForm(): void {
    const account = this.coverSave.account();
    this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.formOpen.set(true);
    queueMicrotask(() => {
      const dialog = this.formDialog()?.nativeElement;
      if (this.formOpen() && account === this.coverSave.account() && dialog !== undefined && !dialog.open) {
        if (typeof dialog.showModal === 'function') {
          dialog.showModal();
        } else {
          dialog.setAttribute('open', '');
        }
      }
    });
  }

  private loadPlatforms(): void {
    const account = this.coverSave.account();
    this.platformsService.list().subscribe({
      next: (platforms) => { if (account === this.coverSave.account()) this.platforms.set(platforms); },
      error: () => { if (account === this.coverSave.account()) this.loadError.set(true); },
    });
  }

  private loadGenres(): void {
    this.genresService.list().subscribe({
      next: (genres) => this.genres.set(genres),
      error: () => this.loadError.set(true),
    });
  }

  protected handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}
