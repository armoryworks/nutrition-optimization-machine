import { AbstractControl, ValidationErrors } from '@angular/forms';

/**
 * Error codes from an ASP.NET Identity failure body. The custom register
 * endpoint returns an array of `{ code, description }`; the Identity API
 * endpoints return a validation-problem object keyed by code.
 */
export function identityErrorCodes(errors: unknown): string[] {
  if (Array.isArray(errors)) {
    return errors
      .map(e => (e && typeof e === 'object' ? (e as { code?: unknown }).code : e))
      .filter((code): code is string => typeof code === 'string' && code.length > 0);
  }
  if (errors && typeof errors === 'object') {
    return Object.keys(errors);
  }
  return [];
}

export interface PasswordRule {
  key: string;
  label: string;
  test: (value: string) => boolean;
}

/**
 * The password rules the API enforces (ASP.NET Identity defaults), with the
 * client's 8-character minimum in place of Identity's 6.
 */
export const PASSWORD_RULES: readonly PasswordRule[] = [
  { key: 'length', label: 'At least 8 characters', test: v => v.length >= 8 },
  { key: 'upper', label: 'An uppercase letter', test: v => /[A-Z]/.test(v) },
  { key: 'lower', label: 'A lowercase letter', test: v => /[a-z]/.test(v) },
  { key: 'digit', label: 'A number', test: v => /[0-9]/.test(v) },
  { key: 'symbol', label: 'A symbol, such as ! or #', test: v => /[^a-zA-Z0-9]/.test(v) },
];

export function passwordRulesValidator(control: AbstractControl): ValidationErrors | null {
  const value = (control.value as string | null) ?? '';
  if (!value) return null;
  const unmet = PASSWORD_RULES.filter(r => !r.test(value)).map(r => r.key);
  return unmet.length ? { passwordRules: unmet } : null;
}
