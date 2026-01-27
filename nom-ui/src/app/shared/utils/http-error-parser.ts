import { HttpErrorResponse } from '@angular/common/http';
import { throwError } from 'rxjs';
import { ERROR_MESSAGES } from '../constants/error-messages';

/**
 * Parse an HttpErrorResponse into a user-friendly error message.
 * Centralizes the HTTP status → message mapping previously duplicated
 * across services (e.g., auth.service.ts handleError).
 */
export function parseHttpError(error: HttpErrorResponse): string {
  if (error.error instanceof ErrorEvent) {
    return error.error.message;
  }

  switch (error.status) {
    case 0:
      return ERROR_MESSAGES.NETWORK_ERROR;
    case 400:
      return error.error?.message || ERROR_MESSAGES.BAD_REQUEST;
    case 401:
      return error.error?.message || ERROR_MESSAGES.UNAUTHORIZED;
    case 403:
      return error.error?.message || ERROR_MESSAGES.FORBIDDEN;
    case 404:
      return error.error?.message || ERROR_MESSAGES.NOT_FOUND;
    case 408:
      return ERROR_MESSAGES.TIMEOUT;
    case 500:
      return error.error?.message || ERROR_MESSAGES.SERVER_ERROR;
    default:
      return error.error?.message || `${ERROR_MESSAGES.SERVER_ERROR} (${error.status})`;
  }
}

/**
 * RxJS-compatible error handler for use with catchError().
 *
 * Usage:
 *   this.http.get('/api/data').pipe(catchError(handleHttpError))
 */
export function handleHttpError(error: HttpErrorResponse) {
  const message = parseHttpError(error);
  console.error('HTTP Error:', error);
  return throwError(() => new Error(message));
}
