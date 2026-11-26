import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';
import { PasswordVisibilityIcon } from '../password-visibility-icon';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, PasswordVisibilityIcon],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly passwordResetComplete = this.router.currentNavigation()?.extras.info === 'passwordResetComplete';

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
  });

  protected readonly submitting = signal(false);
  protected readonly passwordVisible = signal(false);
  protected readonly error = signal('');

  async onSubmit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.error.set('');
    try {
      await this.auth.signIn(this.form.controls.email.value, this.form.controls.password.value);
      await this.router.navigateByUrl('/', { replaceUrl: true });
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Unable to log in. Please try again.');
    } finally {
      this.submitting.set(false);
    }
  }
}
