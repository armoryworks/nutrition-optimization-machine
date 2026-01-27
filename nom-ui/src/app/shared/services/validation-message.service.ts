import { Injectable } from '@angular/core';
import { FormGroup, AbstractControl } from '@angular/forms';
import { IFieldError } from '../interfaces/validation.interfaces';
import { VALIDATION_MESSAGE_TEMPLATES } from '../constants/validation-messages';
import { FIELD_LABELS } from '../constants/field-labels';

@Injectable({ providedIn: 'root' })
export class ValidationMessageService {
  /**
   * Get all field errors from a form group as structured data.
   */
  getFieldErrors(form: FormGroup): IFieldError[] {
    const fieldErrors: IFieldError[] = [];

    this.collectErrors(form, '', fieldErrors);

    return fieldErrors;
  }

  /**
   * Get a human-readable error message for a specific validation error.
   */
  getErrorMessage(errorKey: string, errorValue: any, fieldName: string): string {
    const fieldLabel = this.getFieldLabel(fieldName);
    const template = VALIDATION_MESSAGE_TEMPLATES[errorKey];

    if (!template) {
      return `${fieldLabel} is invalid`;
    }

    return template
      .replace('{field}', fieldLabel)
      .replace('{requiredLength}', errorValue?.requiredLength ?? '')
      .replace('{min}', errorValue?.min ?? '')
      .replace('{max}', errorValue?.max ?? '');
  }

  /**
   * Get human-readable label for a field name.
   */
  getFieldLabel(fieldName: string): string {
    return FIELD_LABELS[fieldName] || this.humanize(fieldName);
  }

  private collectErrors(
    group: FormGroup,
    parentPath: string,
    result: IFieldError[]
  ): void {
    Object.keys(group.controls).forEach((key) => {
      const control: AbstractControl = group.controls[key];
      const fullPath = parentPath ? `${parentPath}.${key}` : key;

      if (control instanceof FormGroup) {
        this.collectErrors(control, fullPath, result);
      } else if (control.errors && (control.touched || control.dirty)) {
        const errors = Object.entries(control.errors).map(([errorKey, errorValue]) => ({
          key: errorKey,
          message: this.getErrorMessage(errorKey, errorValue, key),
        }));

        if (errors.length > 0) {
          result.push({
            fieldName: fullPath,
            fieldLabel: this.getFieldLabel(key),
            errors,
          });
        }
      }
    });
  }

  /**
   * Convert camelCase to human-readable: "firstName" → "First Name"
   */
  private humanize(str: string): string {
    return str
      .replace(/([A-Z])/g, ' $1')
      .replace(/^./, (s) => s.toUpperCase())
      .trim();
  }
}
