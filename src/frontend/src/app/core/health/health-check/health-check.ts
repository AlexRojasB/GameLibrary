import { Component, inject, signal } from '@angular/core';
import { ApiHealthService } from '../api-health.service';
import { BackButton } from '../../../shared/back-button/back-button';

@Component({
  selector: 'app-health-check',
  imports: [BackButton],
  templateUrl: './health-check.html',
  styleUrl: './health-check.scss',
})
export class HealthCheck {
  private readonly apiHealth = inject(ApiHealthService);

  protected readonly state = signal<'loading' | 'ok' | 'error'>('loading');
  protected readonly detail = signal('');

  constructor() {
    this.apiHealth.getHealth().subscribe({
      next: (response) => {
        this.state.set(response.status === 'ok' ? 'ok' : 'error');
        this.detail.set(response.status);
      },
      error: () => {
        this.state.set('error');
        this.detail.set('unreachable');
      },
    });
  }
}
