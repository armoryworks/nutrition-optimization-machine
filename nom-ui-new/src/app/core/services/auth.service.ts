import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, switchMap, catchError, of } from 'rxjs';

export interface AuthTokenResponse {
  tokenType: string;
  accessToken: string;
  expiresIn: number;
  refreshToken: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private isLoggedInSignal = signal(false);
  private usernameSignal = signal('');

  readonly isLoggedIn = this.isLoggedInSignal.asReadonly();
  readonly username = this.usernameSignal.asReadonly();

  get accessToken(): string | null {
    return localStorage.getItem('authToken');
  }

  get refreshToken(): string | null {
    return localStorage.getItem('refreshToken');
  }

  constructor() {
    this.checkLoginStatus();
  }

  login(email: string, password: string): Observable<AuthTokenResponse> {
    return this.http.post<AuthTokenResponse>('/api/auth/login', { email, password }).pipe(
      tap(response => this.storeTokens(response)),
      switchMap(response => this.fetchAndStoreUserInfo().pipe(
        catchError(() => of(null)),
        switchMap(() => of(response))
      ))
    );
  }

  register(email: string, password: string, fullName?: string): Observable<void> {
    return this.http.post<void>('/api/auth/register-custom', {
      email, password, confirmPassword: password, fullName: fullName || null
    });
  }

  /** Call after successful registration to log the user in. */
  loginAfterRegister(email: string, password: string): Observable<AuthTokenResponse> {
    return this.login(email, password);
  }

  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/forgotPassword', { email });
  }

  resetPassword(email: string, token: string, newPassword: string, confirmNewPassword: string): Observable<void> {
    return this.http.post<void>('/api/auth/resetPassword', { email, token, newPassword, confirmNewPassword });
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      tap(() => this.clearSession()),
      catchError(() => {
        this.clearSession();
        return of(undefined);
      })
    );
  }

  attemptTokenRefresh(): Observable<AuthTokenResponse | null> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      this.clearSession();
      return of(null);
    }

    return this.http.post<AuthTokenResponse>('/api/auth/refresh', { refreshToken }).pipe(
      tap(response => this.storeTokens(response)),
      catchError(() => {
        this.clearSession();
        return of(null);
      })
    );
  }

  private storeTokens(response: AuthTokenResponse): void {
    localStorage.setItem('authToken', response.accessToken);
    localStorage.setItem('refreshToken', response.refreshToken);
    this.isLoggedInSignal.set(true);
  }

  private fetchAndStoreUserInfo(): Observable<void> {
    return this.http.get<{ email?: string }>('/api/auth/manage/info').pipe(
      tap(info => {
        const email = info.email ?? '';
        localStorage.setItem('username', email);
        this.usernameSignal.set(email);
      }),
      switchMap(() => of(undefined))
    );
  }

  private checkLoginStatus(): void {
    const token = localStorage.getItem('authToken');
    if (token) {
      this.isLoggedInSignal.set(true);
      this.usernameSignal.set(localStorage.getItem('username') ?? '');
    }
  }

  private clearSession(): void {
    localStorage.removeItem('authToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('username');
    this.isLoggedInSignal.set(false);
    this.usernameSignal.set('');
  }
}
