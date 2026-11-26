import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { PASSWORD_RECOVERY_CLIENT, RECOVERY_CODE } from '../../../core/auth/password-recovery-client';

@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './reset-password.html',
  styleUrl: '../login/login.scss',
})
export class ResetPassword {
  private readonly client = inject(PASSWORD_RECOVERY_CLIENT);
  private readonly router = inject(Router);
  private grant: { userId: string; expiresAt: number } | null = null;
  private destroyed = false;
  protected readonly phase = signal<'checking' | 'ready' | 'invalid' | 'cleanup'>('checking');
  protected readonly submitting = signal(false);
  protected readonly error = signal('');
  protected readonly passwordVisible = signal(false);
  protected readonly form = new FormGroup({
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(6)] }),
  });

  constructor() {
    const code = inject(RECOVERY_CODE)();
    const { data } = this.client.auth.onAuthStateChange((event, session) => {
      if (event === 'SIGNED_OUT' || event === 'SIGNED_IN' || (this.grant && session?.user.id !== this.grant.userId)) {
        this.grant = null;
        if (this.phase() === 'ready') { this.phase.set('invalid'); this.form.reset(); this.passwordVisible.set(false); }
      }
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.grant = null;
      this.form.reset();
      data.subscription.unsubscribe();
      void this.closeRecoverySession().catch(() => undefined);
    });
    void this.verify(code);
  }

  private async verify(code: string | null): Promise<void> {
    // Only an SDK recovery event produced by this fresh, successful exchange may
    // authorize the form. Stored/normal sessions and URL type flags are insufficient.
    let recoveryToken = '';
    const { data: subscription } = this.client.auth.onAuthStateChange((event, session) => {
      if (event === 'PASSWORD_RECOVERY') recoveryToken = session?.access_token ?? '';
    });
    try {
      if (!code) throw new Error('Missing recovery context');
      const { data, error } = await this.client.auth.exchangeCodeForSession(code);
      if (error || !data.session || !recoveryToken || recoveryToken !== data.session.access_token
        || !data.session.expires_at || data.session.expires_at * 1000 <= Date.now()) throw new Error('Invalid recovery context');
      if (this.destroyed) { await this.closeRecoverySession(); return; }
      this.grant = { userId: data.session.user.id, expiresAt: data.session.expires_at * 1000 };
      this.phase.set('ready');
    } catch {
      if (!this.destroyed) this.phase.set('invalid');
      await this.closeRecoverySession().catch(() => undefined);
    } finally {
      subscription.subscription.unsubscribe();
    }
  }

  async onSubmit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.phase() !== 'ready' || this.form.invalid || this.submitting()) return;
    this.submitting.set(true);
    this.error.set('');
    try {
      const grant = this.grant;
      const { data, error } = await this.client.auth.getSession();
      if (this.destroyed) return;
      if (!grant || grant !== this.grant || error || !data.session || data.session.user.id !== grant.userId
        || Date.now() >= grant.expiresAt) throw new Error('Invalid recovery context');
      const result = await this.client.auth.updateUser({ password: this.form.controls.password.value });
      if (result.error) throw result.error;
      this.grant = null;
      this.form.reset();
      this.passwordVisible.set(false);
      if (!this.destroyed) this.phase.set('cleanup');
      await this.finish();
    } catch {
      if (!this.destroyed && this.phase() !== 'cleanup') {
        this.grant = null;
        this.form.reset();
        this.passwordVisible.set(false);
        this.phase.set('invalid');
        this.error.set('Unable to confirm the password update. Try signing in or request another reset email.');
        await this.closeRecoverySession().catch(() => undefined);
      }
    } finally {
      if (!this.destroyed) this.submitting.set(false);
    }
  }

  async retryCleanup(): Promise<void> {
    if (this.phase() !== 'cleanup' || this.submitting()) return;
    this.submitting.set(true);
    await this.finish();
    if (!this.destroyed) this.submitting.set(false);
  }

  private async closeRecoverySession(): Promise<void> {
    const { error } = await this.client.auth.signOut({ scope: 'local' });
    if (error) throw new Error('Recovery session cleanup failed');
  }

  private async finish(): Promise<void> {
    try {
      // Never touch the normal Auth client: its account/session may be unrelated
      // or may have changed while recovery was in flight.
      await this.closeRecoverySession();
      if (this.destroyed) return;
      await this.router.navigateByUrl('/login', { replaceUrl: true, state: { passwordResetComplete: true } });
    } catch {
      if (!this.destroyed) this.error.set('Password updated, but recovery sign-out reported an error. Retry to confirm the recovery session has ended.');
    }
  }
}
