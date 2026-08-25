import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

import { environment } from '../../../../environments/environment';

import { AuthService } from '../../../core/auth/auth.service';

type MeState = 'loading' | 'ok' | 'expired' | 'error';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  protected readonly meState = signal<MeState>('loading');
  protected readonly email = computed(() => this.auth.session()?.user.email ?? '');

  constructor() {
    this.http.get<{ userId: string }>(`${environment.apiBaseUrl}/auth/me`).subscribe({
      next: () => {
        this.meState.set('ok');
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

}
