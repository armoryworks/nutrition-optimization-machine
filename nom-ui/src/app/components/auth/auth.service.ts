import { Injectable } from '@angular/core';
import { Observable, of, throwError } from 'rxjs';
import { catchError, delay, tap } from 'rxjs/operators';
import { RegisterUser } from './models/register-user';
import { LoginUser } from './models/login-user';
import { ForgotPassword } from './models/forgot-password';
import { ResetPassword } from './models/reset-password';
import { ConfirmEmail } from './models/confirm-email';
import { SendConfirmationEmail } from './models/send-confirmation-email';
import { UpdateInfo } from './models/update-info';
import { UpdateTwoFactor } from './models/update-two-factor';
import { LoginResponse } from './models/login-response';
import {
  HttpClient,
  HttpErrorResponse,
  HttpParams,
} from '@angular/common/http';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  constructor(private httpClient: HttpClient) {}

  register(userData: RegisterUser): Observable<void> {
    return this.httpClient
      .post<void>('/api/auth/register', userData)
      .pipe(catchError(this.handleError));
  }

  login(
    credentials: LoginUser,
    useCookies: boolean = false,
    useSessionCookies: boolean = false
  ): Observable<LoginResponse> {
    //not going to code up cookie stuff, for now
    return this.httpClient.post<LoginResponse>('/api/auth/login', credentials);
  }

  logout(): Observable<void> {
    return this.httpClient.post<void>('/api/auth/logout', undefined);
  }

  /**
   * Placeholder for forgot password request.
   * @param data Forgot password request data adhering to ForgotPassword interface.
   * @returns Observable of any.
   */
  forgotPassword(data: ForgotPassword): Observable<any> {
    console.log('Forgot password request for:', data.email);
    // Simulate API call
    return of({
      success: true,
      message: 'Password reset link sent to your email.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock forgot password complete.'))
    );
  }

  /**
   * Placeholder for password reset.
   * @param resetData Password reset details adhering to ResetPassword interface.
   * @returns Observable of any.
   */
  resetPassword(resetData: ResetPassword): Observable<any> {
    console.log('Resetting password:', resetData);
    // Simulate API call
    return of({
      success: true,
      message: 'Your password has been reset successfully.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock password reset complete.'))
    );
  }

  sendConfirmationEmail(data: SendConfirmationEmail): Observable<any> {
    return this.httpClient.post<void>(
      '/api/auth/resendConfirmationEmail',
      data
    );
  }

  /**
   * Placeholder for updating account information (email and/or password).
   * @param updateData UpdateInfo data.
   * @returns Observable of any.
   */
  updateInfo(updateData: UpdateInfo): Observable<any> {
    console.log('Updating account info:', updateData);
    // Simulate API call
    return of({
      success: true,
      message: 'Account information updated successfully.',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock account info update complete.'))
    );
  }

  /**
   * Placeholder for two-factor authentication management.
   * @param data UpdateTwoFactor data.
   * @returns Observable of any.
   */
  updateTwoFactorAuth(data: UpdateTwoFactor): Observable<any> {
    console.log('Updating 2FA settings:', data);
    // Simulate API call
    return of({
      success: true,
      message: 'Two-Factor Authentication settings updated!',
    }).pipe(
      delay(1000),
      tap(() => console.log('Mock 2FA update complete.'))
    );
  }

  /**
   * Placeholder for confirming email, typically via querystring parameters.
   * @param data ConfirmEmail data (userId, code, changedEmail).
   * @returns Observable of any.
   */
  confirmEmail(data: ConfirmEmail): Observable<any> {
    var confirmParams: HttpParams = new HttpParams();
    confirmParams.append('userId', data.userId);
    confirmParams.append('code', data.code);
    if (data.changedEmail) {
      confirmParams.append('changedEmail', data.changedEmail);
    }

    return this.httpClient.get('/api/auth/confirmEmail', {
      params: confirmParams,
    });
  }

  private handleError(error: HttpErrorResponse) {
    let errorMessage = 'An unknown error occurred. Please try again.';

    if (error.error instanceof ErrorEvent) {
      // Client-side or network error occurred.
      errorMessage = `Network error: ${error.error.message}. Please check your internet connection.`;
    } else {
      // The backend returned an unsuccessful response code.
      // The response body may contain clues as to what went wrong.
      if (error.status === 400) {
        // Check if the error response has a structured 'errors' object (like ProblemDetails)
        if (
          error.error &&
          error.error.errors &&
          typeof error.error.errors === 'object'
        ) {
          const validationErrors = error.error.errors;
          const messages: string[] = [];

          // Iterate over the keys of the 'errors' object (e.g., "DuplicateUserName")
          for (const key in validationErrors) {
            if (validationErrors.hasOwnProperty(key)) {
              const errorArray = validationErrors[key];
              // Each key's value is an array of error messages
              if (Array.isArray(errorArray)) {
                messages.push(...errorArray); // Add all messages from this array
              }
            }
          }
          // If specific messages are found, use them. Otherwise, provide a generic 400 error.
          errorMessage =
            messages.length > 0
              ? messages.join('\n')
              : 'Invalid registration data. Please check your inputs.';
        } else if (error.error && typeof error.error === 'string') {
          // Fallback for plain text error messages
          errorMessage = error.error;
        } else {
          errorMessage = 'Invalid registration data. Please check your inputs.';
        }
      } else if (error.status === 500) {
        errorMessage = 'Internal server error. Please try again later.';
      } else {
        errorMessage = `Server responded with status: ${
          error.status
        }. Message: ${error.message || 'No specific message.'}`;
      }
    }
    // Re-throw the error with the processed message.
    // The component will subscribe to this re-thrown error and use NotificationService.
    return throwError(() => new Error(errorMessage));
  }
}
