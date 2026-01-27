import { FormGroup, AbstractControl } from '@angular/forms';
import { AsyncComponentBase } from './async-component-base';

/**
 * Base class for form components extending AsyncComponentBase.
 *
 * Provides form-related helpers:
 * - hasError() for checking field validation state
 * - getErrorId() for ARIA associations
 * - markAllTouched() for triggering validation display
 * - handleSubmit() with pre-validation
 *
 * Usage:
 *   export class MyFormComponent extends FormComponentBase {
 *     form = this.fb.group({ name: ['', Validators.required] });
 *
 *     onSubmit(): void {
 *       this.handleSubmit(this.form, () => {
 *         this.executeSubmit(
 *           this.service.save(this.form.value),
 *           { onSuccess: () => this.router.navigate(['/list']),
 *             errorMessage: ERROR_MESSAGES.RECIPE.SAVE_FAILED }
 *         );
 *       });
 *     }
 *   }
 */
export abstract class FormComponentBase extends AsyncComponentBase {
  /**
   * Check if a form control has a specific error and has been touched/dirty.
   */
  protected hasError(form: FormGroup, fieldName: string, errorKey?: string): boolean {
    const control = form.get(fieldName);
    if (!control) return false;

    if (errorKey) {
      return control.hasError(errorKey) && (control.touched || control.dirty);
    }

    return control.invalid && (control.touched || control.dirty);
  }

  /**
   * Generate a unique error ID for ARIA associations.
   */
  protected getErrorId(fieldName: string): string {
    return `${fieldName}-error`;
  }

  /**
   * Mark all controls in a form as touched to trigger validation display.
   */
  protected markAllTouched(form: FormGroup): void {
    Object.values(form.controls).forEach((control: AbstractControl) => {
      control.markAsTouched();
      control.markAsDirty();

      if ((control as FormGroup).controls) {
        this.markAllTouched(control as FormGroup);
      }
    });
  }

  /**
   * Validate form and execute callback if valid.
   */
  protected handleSubmit(form: FormGroup, onValid: () => void): void {
    this.markAllTouched(form);

    if (form.valid) {
      onValid();
    }
  }
}
