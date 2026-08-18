import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

import { Platform } from '../../platforms/platform';
import { PlatformsService } from '../../platforms/platforms.service';

import { Genre } from '../genres';
import { GenresService } from '../genres.service';
import { VideoGame, VideoGameInput } from '../video-game';
import { VideoGamesService } from '../video-games.service';
import { VideoGameForm } from '../video-game-form/video-game-form';

@Component({
  selector: 'app-video-games-list',
  imports: [RouterLink, VideoGameForm],
  templateUrl: './video-games-list.html',
  styleUrl: './video-games-list.scss',
})
export class VideoGamesList {
  private readonly videoGamesService = inject(VideoGamesService);
  private readonly platformsService = inject(PlatformsService);
  private readonly genresService = inject(GenresService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly videoGames = signal<VideoGame[]>([]);
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly genres = signal<Genre[]>([]);
  protected readonly loadError = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingGame = signal<VideoGame | null>(null);
  protected readonly formError = signal('');
  protected readonly notice = signal('');

  constructor() {
    this.loadPlatforms();
    this.loadGenres();
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.videoGamesService.list().subscribe({
      next: (games) => {
        this.videoGames.set(games);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.loadError.set(true);
      },
    });
  }

  openCreate(): void {
    this.editingGame.set(null);
    this.formError.set('');
    this.formOpen.set(true);
  }

  openEdit(game: VideoGame): void {
    this.editingGame.set(game);
    this.formError.set('');
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.formError.set('');
  }

  onSubmit(input: VideoGameInput): void {
    const editing = this.editingGame();
    if (editing === null) {
      this.createGame(input);
    } else {
      this.updateGame(editing.id, input);
    }
  }

  onDelete(game: VideoGame): void {
    const confirmed = window.confirm(`Delete "${game.name}"?`);
    if (!confirmed) {
      return;
    }
    this.videoGamesService.delete(game.id).subscribe({
      next: () => this.load(),
      error: (error: HttpErrorResponse) => {
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

  private genreName(id: string): string {
    return this.genres().find((genre) => genre.id === id)?.name ?? 'Unknown genre';
  }

  private createGame(input: VideoGameInput): void {
    this.videoGamesService.create(input).subscribe({
      next: () => {
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.formError.set(this.errorMessage(error, 'Unable to save the video game. Please try again.'));
      },
    });
  }

  private updateGame(id: string, input: VideoGameInput): void {
    this.videoGamesService.update(id, input).subscribe({
      next: () => {
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        if (error.status === 404) {
          this.notice.set('This video game no longer exists.');
          this.closeForm();
          this.load();
          return;
        }
        this.formError.set(this.errorMessage(error, 'Unable to save the video game. Please try again.'));
      },
    });
  }

  private errorMessage(error: HttpErrorResponse, fallback: string): string {
    if (error.status === 400) {
      return this.serverDetail(error) ?? 'Please check the video game details.';
    }
    return fallback;
  }

  private serverDetail(error: HttpErrorResponse): string | null {
    const body = error.error as { detail?: unknown } | null;
    if (body !== null && typeof body === 'object' && typeof body.detail === 'string') {
      return body.detail;
    }
    return null;
  }

  private loadPlatforms(): void {
    this.platformsService.list().subscribe({
      next: (platforms) => this.platforms.set(platforms),
      error: () => this.loadError.set(true),
    });
  }

  private loadGenres(): void {
    this.genresService.list().subscribe({
      next: (genres) => this.genres.set(genres),
      error: () => this.loadError.set(true),
    });
  }

  private handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}