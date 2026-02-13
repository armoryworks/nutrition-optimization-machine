import { AbstractControl, ValidatorFn, ValidationErrors } from '@angular/forms';

export interface PasswordRequirement {
  key: string;
  label: string;
  validator: ValidatorFn;
}

export const PASSWORD_REQUIREMENTS: PasswordRequirement[] = [
  {
    key: 'minLength',
    label: 'At least 8 characters',
    validator: (control: AbstractControl): ValidationErrors | null =>
      control.value && control.value.length >= 8 ? null : { minLength: true }
  },
  {
    key: 'requireUppercase',
    label: 'At least one uppercase letter (A\u2013Z)',
    validator: (control: AbstractControl): ValidationErrors | null =>
      control.value && /[A-Z]/.test(control.value) ? null : { requireUppercase: true }
  },
  {
    key: 'requireLowercase',
    label: 'At least one lowercase letter (a\u2013z)',
    validator: (control: AbstractControl): ValidationErrors | null =>
      control.value && /[a-z]/.test(control.value) ? null : { requireLowercase: true }
  },
  {
    key: 'requireDigit',
    label: 'At least one number (0\u20139)',
    validator: (control: AbstractControl): ValidationErrors | null =>
      control.value && /[0-9]/.test(control.value) ? null : { requireDigit: true }
  },
  {
    key: 'requireNonAlphanumeric',
    label: 'At least one special character (!@#$\u2026)',
    validator: (control: AbstractControl): ValidationErrors | null =>
      control.value && /[^a-zA-Z0-9]/.test(control.value) ? null : { requireNonAlphanumeric: true }
  }
];

/** Returns all password ValidatorFns for use in form control definitions */
export function passwordValidators(): ValidatorFn[] {
  return PASSWORD_REQUIREMENTS.map(r => r.validator);
}
