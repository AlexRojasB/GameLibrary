import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal, viewChild } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

import { PlayLogEntry } from '../play-log';
import { LogPlayDialog, UpdatePlayLogEntryDialogRequest } from '../log-play-dialog/log-play-dialog';
import { PlayLogService } from '../play-log.service';

@Component({
  selector: 'app-play-log-page',
  imports: [DatePipe, RouterLink, LogPlayDialog],
  templateUrl: './play-log-page.html',
  styleUrl: './play-log-page.scss',
})
export class PlayLogPage {
  private readonly logPlayDialog = viewChild<LogPlayDialog>('logPlayDialog');
  private readonly playLog = inject(PlayLogService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly entries = signal<PlayLogEntry[]>([]);
  protected readonly error = signal('');

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.error.set('');

    this.playLog.list().subscribe({
      next: (entries) => {
        this.entries.set(sortPlayLogEntries(entries));
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loading.set(false);
        if (error.status === 401) {
          this.auth.clearLocalSession();
          void this.router.navigateByUrl('/login');
          return;
        }
        this.error.set('Unable to load your Play Log. Please try again.');
      },
    });
  }

  protected durationLabel(entry: PlayLogEntry): string {
    if (entry.durationMinutes === null) {
      return '';
    }

    const hours = Math.floor(entry.durationMinutes / 60);
    const minutes = entry.durationMinutes % 60;
    if (hours === 0) {
      return `${minutes} min`;
    }
    if (minutes === 0) {
      return `${hours} h`;
    }
    return `${hours} h ${minutes} min`;
  }

  protected edit(entry: PlayLogEntry): void {
    this.logPlayDialog()?.openEdit(entry);
  }

  protected onUpdateSubmitted(update: UpdatePlayLogEntryDialogRequest): void {
    this.logPlayDialog()?.setPending(true);

    this.playLog.update(update.id, update.request).subscribe({
      next: (entry) => {
        this.entries.update((entries) => sortPlayLogEntries(entries.map((current) => (current.id === entry.id ? entry : current))));
        this.logPlayDialog()?.close();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.logPlayDialog()?.close();
          this.auth.clearLocalSession();
          void this.router.navigateByUrl('/login');
          return;
        }
        const message = this.updateErrorMessage(error);
        this.logPlayDialog()?.showError(message);
      },
    });
  }

  private updateErrorMessage(error: HttpErrorResponse): string {
    const detail = serverDetail(error);
    if (detail !== null) {
      return detail;
    }
    return 'Unable to update this play log entry. Please try again.';
  }
}

function sortPlayLogEntries(entries: PlayLogEntry[]): PlayLogEntry[] {
  return [...entries].sort((a, b) => {
    const playedAt = Date.parse(b.playedAt) - Date.parse(a.playedAt);
    if (playedAt !== 0) {
      return playedAt;
    }

    const createdAt = Date.parse(b.createdAt) - Date.parse(a.createdAt);
    if (createdAt !== 0) {
      return createdAt;
    }

    return b.id.localeCompare(a.id);
  });
}

function serverDetail(error: HttpErrorResponse): string | null {
  if (typeof error.error === 'object' && error.error !== null && 'detail' in error.error && typeof error.error.detail === 'string') {
    return error.error.detail;
  }
  return null;
}
