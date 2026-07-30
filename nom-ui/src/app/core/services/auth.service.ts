import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, switchMap, catchError, of, map, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';
import { PersonModel } from '../models/person.model';
import { AuthTokenResponse } from '../models/auth-token-response.model';
import { CurrentUserModel } from '../models/current-user.model';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private isLoggedInSignal = signal(false);
  private usernameSignal = signal('');
  private personIdSignal = signal<number | null>(null);
  private isAdminSignal = signal(false);
  private adminStatus$: Observable<boolean> | null = null;
  private lastValidated = 0;
  private readonly VALIDATION_INTERVAL = 5 * 60 * 1000; // 5 minutes

  readonly isLoggedIn = this.isLoggedInSignal.asReadonly();
  readonly username = this.usernameSignal.asReadonly();
  readonly personId = this.personIdSignal.asReadonly();
  readonly isAdmin = this.isAdminSignal.asReadonly();

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
      tap((response) => this.storeTokens(response)),
      switchMap((response) =>
        this.fetchAndStoreUserInfo().pipe(
          catchError(() => of(null)),
          switchMap(() => of(response)),
        ),
      ),
    );
  }

  register(email: string, password: string, fullName?: string): Observable<void> {
    return this.http.post<void>('/api/auth/register-custom', {
      email,
      password,
      confirmPassword: password,
      fullName: fullName || null,
    });
  }

  forgotPassword(email: string): Observable<void> {
    return this.http.post<void>('/api/auth/forgotPassword', { email });
  }

  resetPassword(
    email: string,
    token: string,
    newPassword: string,
    confirmNewPassword: string,
  ): Observable<void> {
    return this.http.post<void>('/api/auth/resetPassword', {
      email,
      token,
      newPassword,
      confirmNewPassword,
    });
  }

  confirmEmail(userId: string, token: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/auth/confirm-email', { userId, token });
  }

  resendConfirmation(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/auth/resend-confirmation', { email });
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      tap(() => this.clearSession()),
      catchError(() => {
        this.clearSession();
        return of(undefined);
      }),
    );
  }

  /** Re-issues the bearer token with fresh claims from the DB (e.g. after household create/join). */
  refreshClaims(): Observable<AuthTokenResponse | null> {
    return this.http.post<AuthTokenResponse>('/api/auth/refresh-claims', {}).pipe(
      tap((response) => {
        if (response?.accessToken) {
          this.storeTokens(response);
        }
      }),
      catchError(() => {
        return of(null);
      }),
    );
  }

  attemptTokenRefresh(): Observable<AuthTokenResponse | null> {
    const refreshToken = this.refreshToken;
    if (!refreshToken) {
      this.clearSession();
      return of(null);
    }

    return this.http.post<AuthTokenResponse>('/api/auth/refresh', { refreshToken }).pipe(
      tap((response) => this.storeTokens(response)),
      catchError(() => {
        this.clearSession();
        return of(null);
      }),
    );
  }

  /**
   * Resolves whether the current user holds admin claims (from GET /User/self, which
   * reflects stored claims only). Cached for the session; reset on token change.
   */
  ensureAdminStatus(): Observable<boolean> {
    if (!this.isLoggedInSignal()) {
      return of(false);
    }
    if (!this.adminStatus$) {
      this.adminStatus$ = this.http.get<CurrentUserModel>(`${environment.apiUrl}/User/self`).pipe(
        map((user) => user.isAdmin),
        tap((isAdmin) => this.isAdminSignal.set(isAdmin)),
        catchError(() => {
          this.isAdminSignal.set(false);
          return of(false);
        }),
        shareReplay(1),
      );
    }
    return this.adminStatus$;
  }

  isTokenFresh(): boolean {
    return Date.now() - this.lastValidated < this.VALIDATION_INTERVAL;
  }

  validateToken(): Observable<boolean> {
    return this.http.get('/api/auth/manage/info').pipe(
      tap(() => (this.lastValidated = Date.now())),
      map(() => true),
      catchError(() => {
        this.clearSession();
        return of(false);
      }),
    );
  }

  private storeTokens(response: AuthTokenResponse): void {
    localStorage.setItem('authToken', response.accessToken);
    localStorage.setItem('refreshToken', response.refreshToken);
    this.isLoggedInSignal.set(true);
    this.adminStatus$ = null; // claims may have changed; re-resolve lazily
    this.lastValidated = Date.now();
  }

  private fetchAndStoreUserInfo(): Observable<void> {
    return this.http.get<{ email?: string }>('/api/auth/manage/info').pipe(
      tap((info) => {
        const email = info.email ?? '';
        localStorage.setItem('username', email);
        this.usernameSignal.set(email);
      }),
      switchMap(() => this.fetchAndStorePersonId()),
      catchError(() => of(undefined)),
    );
  }

  private fetchAndStorePersonId(): Observable<void> {
    return this.http.get<PersonModel>(`${environment.apiUrl}/Person/me`).pipe(
      tap((person) => {
        localStorage.setItem('personId', String(person.id));
        this.personIdSignal.set(person.id);
      }),
      switchMap(() => of(undefined)),
      catchError(() => of(undefined)),
    );
  }

  private checkLoginStatus(): void {
    const token = localStorage.getItem('authToken');
    if (token) {
      this.isLoggedInSignal.set(true);
      this.usernameSignal.set(localStorage.getItem('username') ?? '');
      const storedPersonId = localStorage.getItem('personId');
      if (storedPersonId) {
        this.personIdSignal.set(Number(storedPersonId));
      }
    }
  }

  private clearSession(): void {
    localStorage.removeItem('authToken');
    localStorage.removeItem('refreshToken');
    localStorage.removeItem('username');
    localStorage.removeItem('personId');
    this.isLoggedInSignal.set(false);
    this.usernameSignal.set('');
    this.personIdSignal.set(null);
    this.isAdminSignal.set(false);
    this.adminStatus$ = null;
  }
}
