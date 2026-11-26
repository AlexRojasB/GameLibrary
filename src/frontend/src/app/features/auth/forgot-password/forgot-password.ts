import { Component, DestroyRef, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { PASSWORD_RECOVERY_CLIENT, recoveryErrorStatus, recoveryRedirect } from '../../../core/auth/password-recovery-client';

@Component({
  selector: 'app-forgot-password',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './forgot-password.html',
  styleUrl: '../login/login.scss',
})
export class ForgotPassword {
  private readonly client = inject(PASSWORD_RECOVERY_CLIENT);
  private destroyed = false;
  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
  });
  protected readonly submitting = signal(false);
  protected readonly confirmed = signal(false);
  protected readonly error = signal('');

  constructor() { inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.form.reset(); }); }

  async onSubmit(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.submitting() || this.confirmed()) return;
    this.submitting.set(true);
    this.error.set('');
    try {
      const { error } = await this.client.auth.resetPasswordForEmail(this.form.controls.email.value.trim(), { redirectTo: recoveryRedirect() });
      if (error) throw error;
      if (!this.destroyed) this.confirmed.set(true);
    } catch (error) {
      if (this.destroyed) return;
      const status = recoveryErrorStatus(error);
      if (status === 429) this.error.set('Too many requests. Please wait before trying again.');
      else if (status >= 400 && status < 500) this.confirmed.set(true); // Never enumerate accounts.
      else this.error.set('Unable to request a recovery email. Check your connection and try again.');
    } finally {
      if (!this.destroyed) this.submitting.set(false);
    }
  }
}
