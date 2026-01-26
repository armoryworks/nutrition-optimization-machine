/**
 * Centralized error messages for consistent user-facing error text.
 * Use these constants instead of hardcoding error strings in components.
 */
export const ERROR_MESSAGES = {
  // Network/HTTP errors
  NETWORK_ERROR: 'Unable to connect to the server. Please check your internet connection.',
  SERVER_ERROR: 'Server error. Please try again later.',
  UNAUTHORIZED: 'Authentication failed. Please log in again.',
  FORBIDDEN: 'Access denied. You do not have permission to perform this action.',
  NOT_FOUND: 'The requested resource was not found.',
  BAD_REQUEST: 'Invalid request. Please check your input.',
  TIMEOUT: 'The request timed out. Please try again.',
  UNKNOWN: 'An unexpected error occurred. Please try again.',

  // Feature-specific errors
  RECIPE: {
    LOAD_FAILED: 'Failed to load recipe. Please try again.',
    SAVE_FAILED: 'Failed to save recipe. Please try again.',
    DELETE_FAILED: 'Failed to delete recipe. Please try again.',
    SCRAPE_FAILED: 'Failed to import recipe from URL. Please check the URL and try again.',
    TIMELINE_LOAD_FAILED: 'Failed to load timeline events. Please try again.',
    TIMELINE_SAVE_FAILED: 'Failed to create timeline event. Please try again.',
    TIMELINE_DELETE_FAILED: 'Failed to delete timeline event. Please try again.',
    RATING_LOAD_FAILED: 'Failed to load ratings. Please try again.',
    RATING_SAVE_FAILED: 'Failed to save rating. Please try again.',
    RATING_DELETE_FAILED: 'Failed to delete rating. Please try again.',
  },

  MEAL_PLAN: {
    LOAD_FAILED: 'Failed to load meal plan. Please try again.',
    SAVE_FAILED: 'Failed to save meal plan. Please try again.',
    DELETE_FAILED: 'Failed to delete meal plan. Please try again.',
    GENERATE_FAILED: 'Failed to generate meal plan. Please try again.',
  },

  SHOPPING: {
    LOAD_FAILED: 'Failed to load shopping list. Please try again.',
    SAVE_FAILED: 'Failed to save shopping list. Please try again.',
    DELETE_FAILED: 'Failed to delete shopping list. Please try again.',
    ITEM_ADD_FAILED: 'Failed to add item. Please try again.',
  },

  HOUSEHOLD: {
    LOAD_FAILED: 'Failed to load household. Please try again.',
    SAVE_FAILED: 'Failed to save household. Please try again.',
    DELETE_FAILED: 'Failed to delete household. Please try again.',
    INVITE_FAILED: 'Failed to send invitation. Please try again.',
    JOIN_FAILED: 'Failed to join household. Please check the invitation code.',
  },

  AUTH: {
    LOGIN_FAILED: 'Login failed. Please check your credentials.',
    REGISTER_FAILED: 'Registration failed. Please try again.',
    LOGOUT_FAILED: 'Logout failed. Please try again.',
    PASSWORD_RESET_FAILED: 'Failed to reset password. Please try again.',
    EMAIL_CONFIRM_FAILED: 'Failed to confirm email. Please try again.',
    TWO_FACTOR_FAILED: 'Two-factor authentication failed. Please try again.',
  },

  PERSON: {
    LOAD_FAILED: 'Failed to load person. Please try again.',
    SAVE_FAILED: 'Failed to save person. Please try again.',
    DELETE_FAILED: 'Failed to delete person. Please try again.',
    HEALTH_LOAD_FAILED: 'Failed to load health attributes. Please try again.',
  },

  CURATION: {
    LOAD_FAILED: 'Failed to load curation queue. Please try again.',
    APPROVE_FAILED: 'Failed to approve item. Please try again.',
    REJECT_FAILED: 'Failed to reject item. Please try again.',
    REVISION_FAILED: 'Failed to request revision. Please try again.',
    SUBMIT_FAILED: 'Failed to submit for curation. Please try again.',
  },

  COMMUNICATION: {
    LOAD_FAILED: 'Failed to load messages. Please try again.',
    SEND_FAILED: 'Failed to send message. Please try again.',
    DELETE_FAILED: 'Failed to delete message. Please try again.',
  },

  ADMIN: {
    LOAD_USERS_FAILED: 'Failed to load users. Please try again.',
    DELETE_USER_FAILED: 'Failed to delete user. Please try again.',
  },

  MEASUREMENT: {
    LOAD_FAILED: 'Failed to load measurements. Please try again.',
    LOAD_CATEGORIES_FAILED: 'Failed to load measurement categories. Please try again.',
    CONVERT_FAILED: 'Failed to convert measurement. Please try again.',
  },

  PLAN: {
    LOAD_FAILED: 'Failed to load plans. Please try again.',
    CLONE_FAILED: 'Failed to clone plan. Please try again.',
  },

  INGREDIENT: {
    LOAD_FAILED: 'Failed to load ingredients. Please try again.',
    SAVE_FAILED: 'Failed to save ingredient. Please try again.',
    DELETE_FAILED: 'Failed to delete ingredient. Please try again.',
  },

  PRIVACY: {
    LOAD_FAILED: 'Failed to load privacy analytics. Please try again.',
    REPORT_FAILED: 'Failed to generate compliance report. Please try again.',
    EXPORT_FAILED: 'Failed to export analytics. Please try again.',
  },

  CLIPBOARD: {
    COPY_FAILED: 'Failed to copy to clipboard. Please try again.',
  },
} as const;

/**
 * Validation error messages for form fields.
 * Functions return dynamic messages based on validation parameters.
 */
export const VALIDATION_MESSAGES = {
  REQUIRED: 'This field is required',
  EMAIL: 'Please enter a valid email address',
  MIN_LENGTH: (min: number) => `Must be at least ${min} characters`,
  MAX_LENGTH: (max: number) => `Must be no more than ${max} characters`,
  PATTERN: 'Invalid format',
  MIN: (min: number) => `Must be at least ${min}`,
  MAX: (max: number) => `Must be no more than ${max}`,
  PASSWORDS_MISMATCH: 'Passwords do not match',
  URL: 'Please enter a valid URL',
  NUMBER: 'Please enter a valid number',
  POSITIVE_NUMBER: 'Please enter a positive number',
} as const;

export type ErrorMessageKey = keyof typeof ERROR_MESSAGES;
export type ValidationMessageKey = keyof typeof VALIDATION_MESSAGES;
