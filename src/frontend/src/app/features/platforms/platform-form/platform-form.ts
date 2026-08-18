import { Component, effect, input, output } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

@Component({
  selector: 'app-platform-form',
  imports: [ReactiveFormsModule],
  templateUrl: './platform-form.html',
  styleUrl: './platform-form.scss',
})
export class PlatformForm {
  readonly initialName = input('');
  readonly submitLabel = input('Save');
  readonly serverError = input('');

  readonly submitted = output<string>();
  readonly cancelled = output<void>();

  readonly form = new FormGroup({
    name: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(100)],
    }),
  });

  constructor() {
    effect(() => {
      this.form.controls.name.setValue(this.initialName(), { emitEvent: false });
    });
  }

  get name(): FormControl<string> {
    return this.form.controls.name;
  }

  submit(): void {
    const value = this.name.value.trim();

    if (value.length === 0) {
      this.name.markAsTouched();
      this.name.setErrors({ required: true });
      return;
    }

    if (value.length > 100) {
      this.name.markAsTouched();
      this.name.setErrors({ maxlength: { requiredLength: 100, actualLength: value.length } });
      return;
    }

    this.submitted.emit(value);
  }
}