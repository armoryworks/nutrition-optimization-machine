import { Component, input } from '@angular/core';
import { AbstractControl } from '@angular/forms';
import { PASSWORD_REQUIREMENTS } from '../../validators/password-validators';

@Component({
  selector: 'nom-password-requirements',
  standalone: true,
  template: `
    <ul class="password-requirements" role="status" aria-live="polite" aria-label="Password requirements">
      @for (req of requirements; track req.key) {
        <li class="password-requirements__item"
            [class.password-requirements__item--met]="isMet(req.key)"
            [class.password-requirements__item--unmet]="!isMet(req.key) && isDirty()">
          <span class="password-requirements__icon" aria-hidden="true">{{ isMet(req.key) ? '\u2713' : '\u25CB' }}</span>
          <span>{{ req.label }}</span>
        </li>
      }
    </ul>
  `,
  styles: [`
    @use '../../../../variables' as vars;

    .password-requirements {
      list-style: none;
      padding: 0;
      margin: vars.$spacing-1 0 vars.$spacing-2;
      display: flex;
      flex-direction: column;
      gap: 2px;

      &__item {
        display: flex;
        align-items: center;
        gap: vars.$spacing-2;
        font-size: vars.$font-size-xs;
        color: var(--mat-sys-on-surface-variant);
        transition: color vars.$transition-duration-fast vars.$transition-timing;

        &--met {
          color: var(--mat-sys-primary);
        }

        &--unmet {
          color: var(--mat-sys-error);
        }
      }

      &__icon {
        font-size: vars.$font-size-sm;
        width: 1em;
        text-align: center;
      }
    }
  `]
})
export class PasswordRequirementsComponent {
  passwordControl = input.required<AbstractControl>();

  readonly requirements = PASSWORD_REQUIREMENTS;

  isDirty(): boolean {
    return this.passwordControl()?.dirty ?? false;
  }

  isMet(key: string): boolean {
    const control = this.passwordControl();
    if (!control || !control.value) return false;
    return !control.hasError(key);
  }
}
