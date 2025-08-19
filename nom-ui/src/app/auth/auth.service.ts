import { Injectable, inject } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
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
import { CurrentInfo } from './models/current-info';
import { UpdateTwoFactorResponse } from './models/update-two-factor-response';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private httpClient = inject(HttpClient);

  private readonly apiUrl = '/api/auth';



  register(userData: RegisterUser): Observable<void> {
    // Map the frontend model to the API model
    const apiPayload = {
      email: userData.email,
      username: userData.email, // Use email as username
      password: userData.password,
      confirmPassword: userData.confirmPassword,
      fullName: userData.fullName || null,
      groupToken: null,
      householdToken: null
    };

    return this.httpClient
      .post<void>(`${this.apiUrl}/register-custom`, apiPayload)
      .pipe(catchError(this.handleError));
  }

  login(credentials: LoginUser): Observable<LoginResponse> {
    return this.httpClient
      .post<LoginResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(catchError(this.handleError));
  }

  logout(): Observable<void> {
    return this.httpClient
      .post<void>(`${this.apiUrl}/logout`, undefined)
      .pipe(catchError(this.handleError));
  }

  forgotPassword(data: ForgotPassword): Observable<void> {
    return this.httpClient
      .post<void>(`${this.apiUrl}/forgotPassword`, data)
      .pipe(catchError(this.handleError));
  }

  resetPassword(resetData: ResetPassword): Observable<void> {
    return this.httpClient
      .post<void>(`${this.apiUrl}/resetPassword`, resetData)
      .pipe(catchError(this.handleError));
  }

  sendConfirmationEmail(data: SendConfirmationEmail): Observable<void> {
    return this.httpClient
      .post<void>(`${this.apiUrl}/resendConfirmationEmail`, data)
      .pipe(catchError(this.handleError));
  }

  getInfo(): Observable<CurrentInfo> {
    return this.httpClient
      .get<CurrentInfo>(`${this.apiUrl}/manage/info`)
      .pipe(catchError(this.handleError));
  }

  updateInfo(updateData: UpdateInfo): Observable<void> {
    return this.httpClient
      .post<void>(`${this.apiUrl}/manage/info`, updateData)
      .pipe(catchError(this.handleError));
  }

  updateTwoFactorAuth(
    data: UpdateTwoFactor
  ): Observable<UpdateTwoFactorResponse> {
    return this.httpClient
      .post<UpdateTwoFactorResponse>(`${this.apiUrl}/manage/3fa`, data)
      .pipe(catchError(this.handleError));
  }

  confirmEmail(data: ConfirmEmail): Observable<unknown> {
    const confirmParams: HttpParams = new HttpParams();
    confirmParams.append('userId', data.userId);
    confirmParams.append('code', data.code);
    if (data.changedEmail) {
      confirmParams.append('changedEmail', data.changedEmail);
    }

    return this.httpClient.get(`${this.apiUrl}/confirmEmail`, {
      params: confirmParams,
    });
  }

  refreshToken(refreshToken: string): Observable<LoginResponse> {
    return this.httpClient
      .post<LoginResponse>(`${this.apiUrl}/refresh`, { refreshToken })
      .pipe(catchError(this.handleError));
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
            if (Object.prototype.hasOwnProperty.call(validationErrors, key)) {
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
        errorMessage = `Server responded with status: ${error.status
          }. Message: ${error.message || 'No specific message.'}`;
      }
    }
    // Re-throw the error with the processed message.
    // The component will subscribe to this re-thrown error and use NotificationService.
    return throwError(() => new Error(errorMessage));
  }
}
