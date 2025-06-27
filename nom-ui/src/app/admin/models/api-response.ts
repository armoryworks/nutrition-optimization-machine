/**
 * @description Base API response, assuming it might be in this file or a common one.
 * If you have a separate `ApiResponse.model.ts`, you can remove this.
 */
export interface ApiResponse {
  success: boolean;
  message: string;
}
