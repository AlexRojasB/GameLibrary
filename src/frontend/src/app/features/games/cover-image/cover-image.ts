import { HttpErrorResponse } from '@angular/common/http';
import { AfterViewInit, Component, computed, effect, ElementRef, inject, input, OnDestroy, signal, untracked } from '@angular/core';
import { Router } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

import { CoverContentCoordinator } from './cover-content-coordinator.service';

@Component({
  selector: 'app-cover-image',
  templateUrl: './cover-image.html',
  styleUrl: './cover-image.scss',
})
export class CoverImage implements AfterViewInit, OnDestroy {
  readonly coverImageUrl = input<string | null>(null);
  readonly coverUploadId = input<string | null>(null);
  readonly alt = input('');
  readonly imageClass = input('');

  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly auth = inject(AuthService);
  private readonly coordinator = inject(CoverContentCoordinator);
  private readonly router = inject(Router);
  private readonly nearVisible = signal(false);
  private readonly attempt = signal(0);
  private observer: IntersectionObserver | null = null;
  private objectUrl: string | null = null;
  private retryAt = 0;
  protected readonly source = signal<string | null>(null);
  protected readonly loading = signal(false);
  protected readonly failed = signal(false);
  protected readonly retryMessage = signal('');
  private readonly snapshot = signal<{
    external: string | null;
    uploadId: string | null;
    session: ReturnType<AuthService['session']>;
    attempt: number;
  } | null>(null);
  private current: (() => boolean) = () => false;
  protected readonly displaySource = computed(() => {
    const snapshot = this.snapshot();
    const source = this.source();
    return snapshot && snapshot.external === this.coverImageUrl() && snapshot.uploadId === this.coverUploadId()
      && snapshot.session === this.auth.session() ? source : null;
  });

  constructor() {
    effect((onCleanup) => {
      const external = this.coverImageUrl();
      const uploadId = this.coverUploadId();
      const session = this.auth.session();
      const visible = this.nearVisible();
      const attempt = this.attempt();
      let alive = true;
      this.current = () => alive && external === this.coverImageUrl() && uploadId === this.coverUploadId() && session === this.auth.session();
      const current = this.current;
      onCleanup(() => {
        alive = false;
      });

      const previous = untracked(this.snapshot);
      if (!previous || previous.external !== external || previous.uploadId !== uploadId || previous.session !== session || previous.attempt !== attempt) {
        this.disposeUrl();
        this.snapshot.set({ external, uploadId, session, attempt });
        this.source.set(null);
        this.failed.set(false);
        this.retryMessage.set('');
        this.retryAt = 0;
      }
      this.loading.set(false);

      if (!uploadId) {
        if (!untracked(this.failed)) this.source.set(external);
        return;
      }
      if (!session || !visible || untracked(this.source) || untracked(this.failed)) return;
      this.loading.set(true);
      const subscription = this.coordinator.content(uploadId).subscribe({
        next: (blob) => {
          if (!current()) return;
          try {
            this.objectUrl = URL.createObjectURL(blob);
            this.source.set(this.objectUrl);
            this.loading.set(false);
          } catch {
            this.imageFailed();
          }
        },
        error: (error: unknown) => {
          if (!current()) return;
          if (error instanceof HttpErrorResponse && error.status === 401) {
            this.auth.clearLocalSession();
            void this.router.navigateByUrl('/login');
            return;
          }
          this.imageFailed();
          if (error instanceof HttpErrorResponse) {
            const retryAfter = error.headers.get('Retry-After');
            if (retryAfter) {
              const seconds = Number(retryAfter);
              this.retryAt = /^\d+$/.test(retryAfter) ? Date.now() + seconds * 1000 : Date.parse(retryAfter);
              if (Number.isFinite(this.retryAt) && this.retryAt > Date.now()) {
                this.retryMessage.set(`Retry available after ${new Date(this.retryAt).toLocaleTimeString()}.`);
              }
            }
          }
        },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  ngAfterViewInit(): void {
    if (typeof IntersectionObserver === 'undefined') return;
    this.observer = new IntersectionObserver((entries) => {
      this.nearVisible.set(entries.some((entry) => entry.isIntersecting));
    }, { rootMargin: '200px' });
    this.observer.observe(this.host.nativeElement);
  }

  ngOnDestroy(): void {
    this.current = () => false;
    this.observer?.disconnect();
    this.disposeUrl();
  }

  protected imageFailed(event?: Event): void {
    if (!this.current()) return;
    if (event && (event.target as HTMLImageElement).getAttribute('src') !== this.source()) return;
    this.disposeUrl();
    this.source.set(null);
    this.loading.set(false);
    this.failed.set(true);
  }

  protected retry(): void {
    if (!this.current() || Date.now() < this.retryAt) return;
    this.attempt.update((attempt) => attempt + 1);
  }

  private disposeUrl(): void {
    if (this.objectUrl) URL.revokeObjectURL(this.objectUrl);
    this.objectUrl = null;
  }
}
