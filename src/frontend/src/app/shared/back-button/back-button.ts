import { Component, inject, input } from '@angular/core';

import { BackFallback, BackNavigation } from '../../core/navigation/back-navigation';

@Component({
  selector: 'app-back-button',
  template: `<button type="button" class="button button--secondary" [disabled]="navigation.busy()"
    (click)="navigation.back(fallback())"><span aria-hidden="true">←</span> {{ navigation.label(fallback()) }}</button>`,
  styles: `:host { display: block; } button { min-height: 44px; }`,
})
export class BackButton {
  protected readonly navigation = inject(BackNavigation);
  readonly fallback = input<BackFallback>('/');
}
