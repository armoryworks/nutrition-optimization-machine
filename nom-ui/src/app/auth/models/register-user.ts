export interface RegisterUser {
  email: string;
  password: string;
  confirmPassword: string; // Used for client-side validation, may or may not be sent to API
}
