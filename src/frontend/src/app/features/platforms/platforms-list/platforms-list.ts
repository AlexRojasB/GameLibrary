import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

import { Platform } from '../platform';
import { PlatformForm } from '../platform-form/platform-form';
import { PlatformsService } from '../platforms.service';

@Component({
  selector: 'app-platforms-list',
  imports: [RouterLink, PlatformForm],
  templateUrl: './platforms-list.html',
  styleUrl: './platforms-list.scss',
})
export class PlatformsList {
  private readonly platformsService = inject(PlatformsService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly platforms = signal<Platform[]>([]);
  protected readonly loadError = signal(false);
  protected readonly formOpen = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly editingName = signal('');
  protected readonly formError = signal('');
  protected readonly notice = signal('');

  constructor() {
    void this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadError.set(false);
    this.platformsService.list().subscribe({
      next: (platforms) => {
        this.platforms.set(platforms);
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
    this.editingId.set(null);
    this.editingName.set('');
    this.formError.set('');
    this.formOpen.set(true);
  }

  openEdit(platform: Platform): void {
    this.editingId.set(platform.id);
    this.editingName.set(platform.name);
    this.formError.set('');
    this.formOpen.set(true);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.formError.set('');
  }

  onSubmit(name: string): void {
    const editingId = this.editingId();
    if (editingId === null) {
      this.createPlatform(name);
    } else {
      this.updatePlatform(editingId, name);
    }
  }

  onDelete(platform: Platform): void {
    const confirmed = window.confirm(`Delete "${platform.name}"?`);
    if (!confirmed) {
      return;
    }

    this.platformsService.delete(platform.id).subscribe({
      next: () => this.load(),
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        if (error.status === 404) {
          this.notice.set('This platform no longer exists.');
          this.load();
          return;
        }
        if (error.status === 409) {
          this.notice.set('This platform is in use and cannot be deleted.');
          return;
        }
        this.notice.set('Unable to delete the platform. Please try again.');
      },
    });
  }

  private createPlatform(name: string): void {
    this.platformsService.create(name).subscribe({
      next: () => {
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        if (error.status === 401) {
          this.handleSessionExpired();
          return;
        }
        this.formError.set(this.formErrorMessage(error.status));
      },
    });
  }

  private updatePlatform(id: string, name: string): void {
    this.platformsService.update(id, name).subscribe({
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
          this.notice.set('This platform no longer exists.');
          this.closeForm();
          this.load();
          return;
        }
        this.formError.set(this.formErrorMessage(error.status));
      },
    });
  }

  private formErrorMessage(status: number): string {
    if (status === 400) {
      return 'Please check the platform name.';
    }
    if (status === 409) {
      return 'A platform with this name already exists.';
    }
    return 'Unable to save the platform. Please try again.';
  }

  private handleSessionExpired(): void {
    this.auth.clearLocalSession();
    void this.router.navigateByUrl('/login');
  }
}