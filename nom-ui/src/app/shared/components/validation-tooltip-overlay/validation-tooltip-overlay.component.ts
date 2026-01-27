import { Component, inject } from '@angular/core';
import { ValidationTooltipService } from '../../services/validation-tooltip.service';

@Component({
  selector: 'nom-validation-tooltip-overlay',
  standalone: true,
  template: `
    @if (tooltipService.isVisible()) {
      <div class="validation-tooltip"
           [style.bottom.px]="getBottomPosition()"
           [style.left.px]="tooltipService.position().left"
           role="tooltip"
           aria-live="polite">
        <div class="validation-tooltip__header">
          Form has errors:
        </div>
        <ul class="validation-tooltip__errors">
          @for (fieldError of tooltipService.errors(); track fieldError.fieldName) {
            <li class="validation-tooltip__field">
              <strong>{{ fieldError.fieldLabel }}:</strong>
              @for (e of fieldError.errors; track e.key) {
                <span class="validation-tooltip__message">{{ e.message }}</span>
              }
            </li>
          }
        </ul>
      </div>
    }
  `,
  styles: [`
    @use '../../../../variables' as vars;

    .validation-tooltip {
      position: fixed;
      z-index: 1000;
      background-color: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
      border: 1px solid var(--mat-sys-error);
      border-radius: vars.$nom-border-radius;
      padding: vars.$spacing-3 vars.$spacing-4;
      max-width: 400px;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      pointer-events: none;

      &__header {
        font-size: vars.$font-size-sm;
        font-weight: vars.$font-weight-semibold;
        margin-bottom: vars.$spacing-2;
      }

      &__errors {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      &__field {
        font-size: vars.$font-size-sm;
        padding: vars.$spacing-1 0;

        strong {
          margin-right: vars.$spacing-1;
        }
      }

      &__message {
        display: inline;

        &::before {
          content: '';
        }
      }
    }
  `]
})
export class ValidationTooltipOverlayComponent {
  tooltipService = inject(ValidationTooltipService);

  getBottomPosition(): number {
    return window.innerHeight - this.tooltipService.position().top;
  }
}
