import { Component, input } from '@angular/core';
import { AmwProgressSpinnerComponent, AmwProgressBarComponent } from 'angular-material-wrap';

/**
 * Standardized loading state display.
 * Replaces the repeated pattern across 60+ components:
 *
 *   @if (isLoading()) {
 *     <div role="status"><amw-progress-bar mode="indeterminate"/></div>
 *   }
 *
 * Usage:
 *   <nom-loading-state message="Loading recipe..."/>
 *   <nom-loading-state variant="bar"/>
 */
@Component({
  selector: 'nom-loading-state',
  standalone: true,
  imports: [AmwProgressSpinnerComponent, AmwProgressBarComponent],
  template: `
    <div class="nom-loading-state" [class.nom-loading-state--inline]="variant() === 'bar'" role="status"
         [attr.aria-label]="message()">
      @if (variant() === 'spinner') {
        <amw-progress-spinner [diameter]="spinnerSize()" mode="indeterminate"/>
        @if (showMessage()) {
          <p class="nom-loading-state__message">{{ message() }}</p>
        }
      } @else {
        <amw-progress-bar mode="indeterminate"/>
        @if (showMessage()) {
          <p class="nom-loading-state__message">{{ message() }}</p>
        }
      }
    </div>
  `,
  styles: [`
    @use '../../../../variables' as vars;

    .nom-loading-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: vars.$spacing-8;
      gap: vars.$spacing-4;

      &--inline {
        padding: vars.$spacing-2 0;
      }

      &__message {
        color: var(--mat-sys-on-surface-variant);
        font-size: vars.$font-size-sm;
        margin: 0;
      }
    }
  `]
})
export class LoadingStateComponent {
  message = input<string>('Loading...');
  variant = input<'spinner' | 'bar'>('spinner');
  spinnerSize = input<number>(40);
  showMessage = input<boolean>(true);
}
