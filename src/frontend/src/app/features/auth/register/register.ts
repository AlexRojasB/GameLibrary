import { Component, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.scss',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly form = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.minLength(6)] }),
  });

  protected readonly submitting = signal(false);
  protected readonly error = signal('');
  protected readonly needsEmailConfirmation = signal(false);

  async onSubmit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      return;
    }
    this.submitting.set(true);
    this.error.set('');
    this.needsEmailConfirmation.set(false);
    try {
      const { needsEmailConfirmation } = await this.auth.signUp(
        this.form.controls.email.value,
        this.form.controls.password.value,
      );
      if (needsEmailConfirmation) {
        this.needsEmailConfirmation.set(true);
      } else {
        await this.router.navigateByUrl('/');
      }
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'Unable to create your account. Please try again.');
    } finally {
      this.submitting.set(false);
    }
  }
}