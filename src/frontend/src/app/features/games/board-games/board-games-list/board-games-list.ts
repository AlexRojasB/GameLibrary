import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, ElementRef, inject, signal, untracked, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from '../../../../core/auth/auth.service';

import { BoardGame, BoardGameInput } from '../../board-game';
import { BoardGamesService } from '../../board-games.service';
import { BoardGameForm } from '../board-game-form/board-game-form';
import { CoverSave, metadataChanged } from '../../covers/cover-save';
import { CoverUploadsService } from '../../covers/cover-uploads.service';
import { CoverImage } from '../../cover-image/cover-image';

@Component({
  selector: 'app-board-games-list',
  imports: [RouterLink, BoardGameForm, CoverImage],
  providers: [CoverSave],
  templateUrl: './board-games-list.html',
  styleUrl: './board-games-list.scss',
})
export class BoardGamesList {
  private readonly formDialog = viewChild<ElementRef<HTMLDialogElement>>('formDialog');
  private readonly boardGamesService = inject(BoardGamesService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly coverSave = inject(CoverSave);
  private readonly coverUploads = inject(CoverUploadsService);
  private loadGeneration = 0;

  protected readonly loading = signal(true);
  protected readonly boardGames = signal<BoardGame[]>([]);
  protected readonly loadError = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingGame = signal<BoardGame | null>(null);
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
          this.boardGames.set([]);
          this.editingGame.set(null);
          this.notice.set('');
          if (current !== null) this.load();
        });
      }
    });
    this.load();
  }

  load(): void {
    const generation = ++this.loadGeneration;
    const account = this.coverSave.account();
    this.loading.set(true);
    this.loadError.set(false);
    this.boardGamesService.list().subscribe({
      next: (games) => {
        if (generation !== this.loadGeneration || account !== this.coverSave.account()) return;
        this.boardGames.set(games);
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

  openCreate(): void {
    if (!this.coverSave.begin(null)) return;
    this.editingGame.set(null);
    this.openForm();
  }

  openEdit(game: BoardGame): void {
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

  onSubmit(input: BoardGameInput): void {
    const editing = this.editingGame();
    const coverOnly = editing !== null && !metadataChanged(input, editing);
    this.coverSave.save({
      gameId: editing?.id ?? null,
      write: (coverUpdate) => coverOnly && coverUpdate.intent !== 'keep'
        ? this.coverUploads.update(editing!.id, coverUpdate)
        : editing === null ? this.boardGamesService.create({ ...input, coverUpdate })
          : this.boardGamesService.update(editing.id, { ...input, coverUpdate }),
      refetch: (id) => this.boardGamesService.list().pipe(map((games) => games.find((game) => game.id === id) ?? null)),
      saved: () => { this.closeForm(); this.load(); },
      unauthorized: () => this.handleSessionExpired(),
      missing: () => { this.notice.set('This board game no longer exists.'); this.closeForm(); this.load(); },
    });
  }

  onDelete(game: BoardGame): void {
    if (this.coverSave.busy() || this.coverSave.recovery()) return;
    const account = this.coverSave.account();
    const confirmed = window.confirm(`Delete "${game.name}"?`);
    if (!confirmed) {
      return;
    }
    this.boardGamesService.delete(game.id).subscribe({
      next: () => { if (account === this.coverSave.account()) this.load(); },
      error: (error: HttpErrorResponse) => {
        if (account !== this.coverSave.account()) return;
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

  protected handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}
