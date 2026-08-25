import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../../core/auth/auth.service';

import { BoardGame, BoardGameInput } from '../../board-game';
import { BoardGamesService } from '../../board-games.service';
import { BoardGameForm } from '../board-game-form/board-game-form';

@Component({
  selector: 'app-board-games-list',
  imports: [RouterLink, BoardGameForm],
  templateUrl: './board-games-list.html',
  styleUrl: './board-games-list.scss',
})
export class BoardGamesList {
  private readonly formDialog = viewChild<ElementRef<HTMLDialogElement>>('formDialog');
  private readonly boardGamesService = inject(BoardGamesService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly boardGames = signal<BoardGame[]>([]);
  protected readonly loadError = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingGame = signal<BoardGame | null>(null);
  protected readonly formError = signal('');
  protected readonly notice = signal('');
  private returnFocusTo: HTMLElement | null = null;

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.boardGamesService.list().subscribe({
      next: (games) => {
        this.boardGames.set(games);
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
    this.openForm();
  }

  openEdit(game: BoardGame): void {
    this.editingGame.set(game);
    this.formError.set('');
    this.openForm();
  }

  closeForm(): void {
    const dialog = this.formDialog()?.nativeElement;
    if (dialog?.open) {
      if (typeof dialog.close === 'function') {
        dialog.close();
      } else {
        dialog.removeAttribute('open');
      }
    }
    this.formOpen.set(false);
    this.formError.set('');
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

  onSubmit(input: BoardGameInput): void {
    const editing = this.editingGame();
    if (editing === null) {
      this.createGame(input);
    } else {
      this.updateGame(editing.id, input);
    }
  }

  onDelete(game: BoardGame): void {
    const confirmed = window.confirm(`Delete "${game.name}"?`);
    if (!confirmed) {
      return;
    }
    this.boardGamesService.delete(game.id).subscribe({
      next: () => this.load(),
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        if (error.status === 404) {
          this.notice.set('This board game no longer exists.');
          this.load();
          return;
        }
        this.notice.set('Unable to delete the board game. Please try again.');
      },
    });
  }

  protected playerRange(game: BoardGame): string {
    if (game.minimumPlayers === 1 && game.maximumPlayers === 1) {
      return '1 player';
    }
    return `${game.minimumPlayers}–${game.maximumPlayers} players`;
  }

  private openForm(): void {
    this.returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    this.formOpen.set(true);
    queueMicrotask(() => {
      const dialog = this.formDialog()?.nativeElement;
      if (dialog !== undefined && !dialog.open) {
        if (typeof dialog.showModal === 'function') {
          dialog.showModal();
        } else {
          dialog.setAttribute('open', '');
        }
      }
    });
  }

  private createGame(input: BoardGameInput): void {
    this.boardGamesService.create(input).subscribe({
      next: () => {
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.formError.set(this.errorMessage(error, 'Unable to save the board game. Please try again.'));
      },
    });
  }

  private updateGame(id: string, input: BoardGameInput): void {
    this.boardGamesService.update(id, input).subscribe({
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
          this.notice.set('This board game no longer exists.');
          this.closeForm();
          this.load();
          return;
        }
        this.formError.set(this.errorMessage(error, 'Unable to save the board game. Please try again.'));
      },
    });
  }

  private errorMessage(error: HttpErrorResponse, fallback: string): string {
    if (error.status === 400) {
      return this.serverDetail(error) ?? 'Please check the board game details.';
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

  protected handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}
