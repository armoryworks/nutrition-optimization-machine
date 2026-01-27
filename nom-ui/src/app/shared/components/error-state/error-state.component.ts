import { Component, input, output } from '@angular/core';
import { AmwButtonComponent, AmwIconComponent } from 'angular-material-wrap';

/**
 * Standardized error state display.
 * Replaces the repeated pattern across 15+ components:
 *
 *   @if (error()) {
 *     <div class="nom-error-state" role="alert">
 *       <amw-icon name="error_outline"/>
 *       <p>{{ error() }}</p>
 *       <amw-button (click)="onRetry()">Retry</amw-button>
 *     </div>
 *   }
 *
 * Usage:
 *   <nom-error-state [message]="error()!" (retry)="onRetry()"/>
 */
@Component({
  selector: 'nom-error-state',
  standalone: true,
  imports: [AmwButtonComponent, AmwIconComponent],
  template: `
    <div class="nom-error-state" role="alert">
      <amw-icon [name]="icon()"/>
      <p class="nom-error-state__message">{{ message() }}</p>
      <div class="nom-error-state__actions">
        @if (showRetryButton()) {
          <amw-button variant="outlined" (click)="retry.emit()">
            <amw-icon name="refresh"/>
            Retry
          </amw-button>
        }
        @if (showBackButton()) {
          <amw-button variant="text" (click)="back.emit()">
            Back
          </amw-button>
        }
      </div>
    </div>
  `,
  styles: [`
    @use '../../../../variables' as vars;

    .nom-error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: vars.$spacing-8;
      text-align: center;

      &__message {
        color: var(--mat-sys-on-error-container);
        font-size: vars.$font-size-md;
        margin: vars.$spacing-3 0 vars.$spacing-6;
      }

      &__actions {
        display: flex;
        gap: vars.$spacing-3;
      }
    }
  `]
})
export class ErrorStateComponent {
  message = input.required<string>();
  icon = input<string>('error_outline');
  showRetryButton = input<boolean>(true);
  showBackButton = input<boolean>(false);

  retry = output<void>();
  back = output<void>();
}
