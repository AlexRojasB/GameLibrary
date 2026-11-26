import { Component, input } from '@angular/core';

// Decorative only: the existing native button owns the action name and behavior.
@Component({
  selector: 'app-password-visibility-icon',
  template: `<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
    stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">
    <path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12Z" />
    <circle cx="12" cy="12" r="3" />
    @if (visible()) { <line x1="3" y1="3" x2="21" y2="21" /> }
  </svg>`,
  styles: ':host { display: inline-flex; }',
})
export class PasswordVisibilityIcon {
  readonly visible = input.required<boolean>();
}
