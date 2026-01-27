/**
 * Human-readable labels for common form field names.
 * Used by the validation tooltip to display friendly field names.
 */
export const FIELD_LABELS: Record<string, string> = {
  // Auth
  email: 'Email',
  password: 'Password',
  confirmPassword: 'Confirm Password',
  currentPassword: 'Current Password',
  newPassword: 'New Password',
  username: 'Username',

  // Person
  firstName: 'First Name',
  lastName: 'Last Name',
  personName: 'Name',
  dateOfBirth: 'Date of Birth',

  // General
  name: 'Name',
  title: 'Title',
  description: 'Description',
  notes: 'Notes',
  url: 'URL',

  // Household
  householdName: 'Household Name',
  invitationCode: 'Invitation Code',

  // Recipe
  recipeName: 'Recipe Name',
  servings: 'Servings',
  prepTime: 'Prep Time',
  cookTime: 'Cook Time',
  ingredients: 'Ingredients',
  instructions: 'Instructions',
  quantity: 'Quantity',
  unit: 'Unit',

  // Meal Plan
  startDate: 'Start Date',
  endDate: 'End Date',
  expiresInDays: 'Expiration Days',

  // Shopping
  itemName: 'Item Name',
  category: 'Category',

  // Communication
  subject: 'Subject',
  message: 'Message',
  commentText: 'Comment',

  // Two Factor
  verificationCode: 'Verification Code',
  twoFactorCode: 'Two-Factor Code',
};
