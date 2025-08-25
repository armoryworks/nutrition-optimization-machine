import { Injectable, inject } from '@angular/core';
import { Observable, throwError, BehaviorSubject } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { environment } from '../../environments/environment';
import { LoginUser } from './models/login-user';
import { LoginResponse } from './models/login-response';
import { RegisterUser } from './models/register-user';
import { ForgotPassword } from './models/forgot-password';
import { ResetPassword } from './models/reset-password';
import { ConfirmEmail } from './models/confirm-email';
import { SendConfirmationEmail } from './models/send-confirmation-email';
import { UpdateInfo } from './models/update-info';
import { UpdateTwoFactor } from './models/update-two-factor';
import { CurrentInfo } from './models/current-info';
import { UpdateTwoFactorResponse } from './models/update-two-factor-response';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private httpClient = inject(HttpClient);
  private apiUrl = environment.apiUrl;

  private isLoggedInSubject = new BehaviorSubject<boolean>(false);
  isLoggedIn$ = this.isLoggedInSubject.asObservable();

  constructor() {
    this.checkLoginStatus();
  }

  private checkLoginStatus(): void {
    const token = localStorage.getItem('authToken');
    if (token) {
      this.isLoggedInSubject.next(true);
    }
  }

  login(credentials: LoginUser): Observable<LoginResponse> {
    return this.httpClient.post<LoginResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(
        tap((response: LoginResponse) => {
          if (response.accessToken) {
            localStorage.setItem('authToken', response.accessToken);
            this.isLoggedInSubject.next(true);
          }
        }),
        catchError(this.handleError)
      );
  }

  logout(): Observable<void> {
    localStorage.removeItem('authToken');
    this.isLoggedInSubject.next(false);
    return this.httpClient.post<void>(`${this.apiUrl}/logout`, undefined).pipe(catchError(this.handleError));
  }

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
      // Client-side error
      errorMessage = error.error.message;
    } else {
      // Server-side error
      if (error.status === 0) {
        errorMessage = 'Unable to connect to the server. Please check your internet connection.';
      } else if (error.status === 400) {
        errorMessage = error.error?.message || 'Bad request. Please check your input.';
      } else if (error.status === 401) {
        errorMessage = error.error?.message || 'Authentication failed. Please log in again.';
      } else if (error.status === 403) {
        errorMessage = error.error?.message || 'Access denied. You do not have permission to perform this action.';
      } else if (error.status === 404) {
        errorMessage = error.error?.message || 'The requested resource was not found.';
      } else if (error.status === 500) {
        errorMessage = error.error?.message || 'Server error. Please try again later.';
      } else {
        errorMessage = error.error?.message || `Server error (${error.status}). Please try again.`;
      }
    }

    console.error('AuthService error:', error);
    return throwError(() => new Error(errorMessage));
  }
}
