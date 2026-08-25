import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { AuthService } from './core/auth/auth.service';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly auth = inject(AuthService);
  protected readonly router = inject(Router);

  protected readonly signingOut = signal(false);
  protected readonly email = computed(() => this.auth.session()?.user.email ?? '');

  protected get showShell(): boolean {
    const path = this.router.url.split('?')[0];
    return path !== '/login' && path !== '/register' && path !== '/health';
  }

  protected isManageActive(): boolean {
    const path = this.router.url.split('?')[0];
    return ['/manage', '/video-games', '/board-games', '/platforms'].some((route) => path.startsWith(route));
  }

  protected async onLogout(): Promise<void> {
    this.signingOut.set(true);
    try {
      await this.auth.signOut();
      await this.router.navigateByUrl('/login');
    } finally {
      this.signingOut.set(false);
    }
  }
}
