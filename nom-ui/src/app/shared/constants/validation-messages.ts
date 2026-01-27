/**
 * Template-based validation messages.
 * {field} is replaced with the human-readable field label.
 * Other placeholders are replaced from the Angular validation error value.
 */
export const VALIDATION_MESSAGE_TEMPLATES: Record<string, string> = {
  required: '{field} is required',
  email: 'Please enter a valid email address',
  minlength: '{field} must be at least {requiredLength} characters',
  maxlength: '{field} cannot exceed {requiredLength} characters',
  min: '{field} must be at least {min}',
  max: '{field} cannot exceed {max}',
  pattern: '{field} format is invalid',
  passwordMismatch: 'Passwords do not match',
  invalidUrl: 'Please enter a valid URL',
  positiveNumber: '{field} must be a positive number',
  arrayMinLength: '{field} must have at least {min} items',
};
