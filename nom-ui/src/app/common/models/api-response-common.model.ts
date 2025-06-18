/**
 * Model for common API response structures (e.g., for success/error messages).
 */
export interface ApiResponseCommonModel {
  message?: string;
  success?: boolean;
  // Add other common response properties as needed (e.g., data payload)
  data?: any;
}
