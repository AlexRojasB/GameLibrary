import { HttpClient } from '@angular/common/http';
import { DestroyRef, effect, inject, Injectable } from '@angular/core';
import { Session } from '@supabase/supabase-js';
import { Observable, Subscriber, Subscription } from 'rxjs';

import { AuthService } from '../../../core/auth/auth.service';
import { API_BASE_URL } from '../../../core/auth/token.interceptor';

interface ContentRequest {
  id: string;
  session: Session;
  listeners: Set<Subscriber<Blob>>;
  subscription: Subscription | null;
}

@Injectable({ providedIn: 'root' })
export class CoverContentCoordinator {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly apiBaseUrl = inject(API_BASE_URL).replace(/\/$/, '');
  private session = this.auth.session();
  private readonly requests = new Map<string, ContentRequest>();
  private pumping = false;

  constructor() {
    effect(() => {
      this.auth.session();
      this.synchronizeSession();
    });
    inject(DestroyRef).onDestroy(() => this.cancelAll());
  }

  content(uploadId: string): Observable<Blob> {
    return new Observable((listener) => {
      this.synchronizeSession();
      const session = this.session;
      if (!session || !/^[\da-f]{8}-(?:[\da-f]{4}-){3}[\da-f]{12}$/i.test(uploadId) || !this.apiBaseUrl) {
        listener.error(new Error('Cover unavailable.'));
        return;
      }

      const id = uploadId.toLowerCase();
      let request = this.requests.get(id);
      if (!request) {
        request = { id, session, listeners: new Set(), subscription: null };
        this.requests.set(id, request);
      }
      request.listeners.add(listener);
      this.pump();
      const current = request;
      return () => {
        current.listeners.delete(listener);
        if (current.listeners.size === 0 && this.requests.get(id) === current) {
          this.requests.delete(id);
          current.subscription?.unsubscribe();
          this.pump();
        }
      };
    });
  }

  private synchronizeSession(): void {
    const session = this.auth.session();
    if (session !== this.session) {
      // Session snapshots also invalidate a same-user logout/login (or token replacement).
      this.session = session;
      this.cancelAll();
    }
  }

  private cancelAll(): void {
    const requests = [...this.requests.values()];
    this.requests.clear();
    for (const request of requests) {
      request.subscription?.unsubscribe();
      for (const listener of request.listeners) listener.complete();
    }
  }

  private pump(): void {
    if (this.pumping) return;
    this.pumping = true;
    try {
      this.synchronizeSession();
      while ([...this.requests.values()].filter((request) => request.subscription !== null).length < 4) {
        const request = [...this.requests.values()].find((request) => request.subscription === null);
        if (!request) break;
        const subscription = new Subscription();
        request.subscription = subscription;
        subscription.add(this.http.get(`${this.apiBaseUrl}/cover-uploads/${request.id}/content`, {
          responseType: 'blob',
          cache: 'no-store',
          headers: { 'ngsw-bypass': 'true' },
        }).subscribe({
          next: (blob) => this.finish(request, blob),
          error: (error: unknown) => this.finish(request, null, error),
        }));
      }
    } finally {
      this.pumping = false;
    }
  }

  private finish(request: ContentRequest, blob: Blob | null, error?: unknown): void {
    this.synchronizeSession();
    if (this.requests.get(request.id) !== request || request.session !== this.auth.session()) return;
    // Evict before notifying: even a reentrant subscriber must get fresh authorization.
    this.requests.delete(request.id);
    request.subscription?.unsubscribe();
    for (const listener of request.listeners) {
      if (blob) {
        listener.next(blob);
        listener.complete();
      } else {
        listener.error(error);
      }
    }
    this.pump();
  }
}
