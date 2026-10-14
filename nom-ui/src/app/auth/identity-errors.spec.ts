import { FormControl } from '@angular/forms';
import { identityErrorCodes, PASSWORD_RULES, passwordRulesValidator } from './identity-errors';

describe('identityErrorCodes', () => {
  it('reads codes from the register endpoint\'s error array', () => {
    expect(identityErrorCodes([
      { code: 'PasswordRequiresNonAlphanumeric', description: 'x' },
      { code: 'PasswordRequiresUpper', description: 'y' },
    ])).toEqual(['PasswordRequiresNonAlphanumeric', 'PasswordRequiresUpper']);
  });

  it('reads codes from a validation-problem object', () => {
    expect(identityErrorCodes({ PasswordTooShort: ['too short'] })).toEqual(['PasswordTooShort']);
  });

  it('ignores anything else', () => {
    expect(identityErrorCodes(null)).toEqual([]);
    expect(identityErrorCodes('oops')).toEqual([]);
    expect(identityErrorCodes([{ description: 'no code' }])).toEqual([]);
  });
});

describe('passwordRulesValidator', () => {
  const unmet = (value: string) => passwordRulesValidator(new FormControl(value))?.['passwordRules'] ?? [];

  it('accepts a password meeting every Identity rule', () => {
    expect(passwordRulesValidator(new FormControl('Abcdefg1!'))).toBeNull();
  });

  it('names each unmet rule', () => {
    expect(unmet('Abcdefgh12345678')).toEqual(['symbol']);
    expect(unmet('abc')).toEqual(['length', 'upper', 'digit', 'symbol']);
  });

  it('treats non-ASCII letters as symbols, as Identity does', () => {
    expect(PASSWORD_RULES.find(r => r.key === 'symbol')!.test('é')).toBe(true);
  });

  it('leaves an empty value to the required validator', () => {
    expect(passwordRulesValidator(new FormControl(''))).toBeNull();
  });
});
