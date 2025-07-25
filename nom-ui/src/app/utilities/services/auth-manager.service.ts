// File: nom-ui/src/app/utilities/services/auth-manager.service.ts

import { Injectable } from '@angular/core';
import { BehaviorSubject, Subject, Observable, throwError, of } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import {
  tap,
  catchError,
  switchMap,
  finalize,
  filter,
  take,
} from 'rxjs/operators';
import { NotificationService } from '../../utilities/services/notification.service';

// Define interfaces for API responses/requests from your baseline
interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
  personId: number;
}

interface RefreshTokenResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

interface LoginRequest {
  email: string;
  password: string;
}

@Injectable({
  providedIn: 'root',
})
export class AuthManagerService {
  public userLogin: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(false);
  public openUserMenuSignal: Subject<void> = new Subject<void>();

  // NEW: Observables for administrative roles
  private _canManageCuration = new BehaviorSubject<boolean>(false);
  private _canManageUserRoles = new BehaviorSubject<boolean>(false);
  public readonly canManageCuration$ = this._canManageCuration.asObservable();
  public readonly canManageUserRoles$ = this._canManageUserRoles.asObservable();

  private apiUrl = 'api/Auth';
  private isRefreshing = false;
  private refreshTokenSubject: BehaviorSubject<any> = new BehaviorSubject<any>(null);

  private readonly TOKEN_KEY = 'nom-token';
  private readonly REFRESH_TOKEN_KEY = 'nom-refresh-token';
  private readonly EXPIRATION_KEY = 'nom-token-expiration';
  private readonly REMEMBER_ME_KEY = 'nom-remember-me';
  private readonly PERSON_ID_KEY = 'nom-person-id';

  private _accessToken?: string;
  private _refreshToken?: string;
  private _tokenExpiration?: number;
  private _rememberMe: boolean = false;
  private _personId?: number;
  private storage!: Storage;

  constructor(
    private http: HttpClient,
    private router: Router,
    private notificationService: NotificationService
  ) {
    this._rememberMe = localStorage.getItem(this.REMEMBER_ME_KEY) === 'true';
    this.storage = this._rememberMe ? localStorage : sessionStorage;

    this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    const storedExpiration = this.storage.getItem(this.EXPIRATION_KEY);
    this._tokenExpiration = storedExpiration ? parseInt(storedExpiration, 10) : undefined;
    this._refreshToken = this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;
    const storedPersonId = this.storage.getItem(this.PERSON_ID_KEY);
    this._personId = storedPersonId ? parseInt(storedPersonId, 10) : undefined;

    this.checkUserLoggedInStatus();
  }

  set token(value: string | undefined) {
    this._accessToken = value;
    if (this._accessToken) {
      this.storage.setItem(this.TOKEN_KEY, this._accessToken);
      this.decodeAndSetClaims(this._accessToken);
    } else {
      this.storage.removeItem(this.TOKEN_KEY);
      this.clearClaims();
    }
    this.userLogin.next(!!this._accessToken);
  }

  get token(): string | undefined {
    if (!this._accessToken) {
      this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    }
    return this._accessToken;
  }

  set storedRefreshToken(value: string | undefined) {
    this._refreshToken = value;
    if (this._refreshToken) {
      this.storage.setItem(this.REFRESH_TOKEN_KEY, this._refreshToken);
    } else {
      this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    }
  }

  get storedRefreshToken(): string | undefined {
    if (!this._refreshToken) {
      this._refreshToken = this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;
    }
    return this._refreshToken;
  }

  set tokenExpiration(value: number | undefined) {
    this._tokenExpiration = value;
    if (this._tokenExpiration) {
      this.storage.setItem(this.EXPIRATION_KEY, this._tokenExpiration.toString());
    } else {
      this.storage.removeItem(this.EXPIRATION_KEY);
    }
  }

  get tokenExpiration(): number | undefined {
    if (!this._tokenExpiration) {
      const expiration = this.storage.getItem(this.EXPIRATION_KEY);
      this._tokenExpiration = expiration ? parseInt(expiration, 10) : undefined;
    }
    return this._tokenExpiration;
  }

  set personId(value: number | undefined) {
    this._personId = value;
    if (this._personId !== undefined) {
      this.storage.setItem(this.PERSON_ID_KEY, this._personId.toString());
    } else {
      this.storage.removeItem(this.PERSON_ID_KEY);
    }
  }

  get personId(): number | undefined {
    if (this._personId === undefined) {
      const storedId = this.storage.getItem(this.PERSON_ID_KEY);
      this._personId = storedId ? parseInt(storedId, 10) : undefined;
    }
    return this._personId;
  }

  set rememberMe(value: boolean) {
    if (this._rememberMe !== value) {
      this._rememberMe = value;
      localStorage.setItem(this.REMEMBER_ME_KEY, value.toString());

      const oldStorage = this.storage;
      this.storage = this._rememberMe ? localStorage : sessionStorage;

      if (oldStorage !== this.storage) {
        const currentToken = oldStorage.getItem(this.TOKEN_KEY);
        const currentRefreshToken = oldStorage.getItem(this.REFRESH_TOKEN_KEY);
        const currentExpiration = oldStorage.getItem(this.EXPIRATION_KEY);
        const currentPersonId = oldStorage.getItem(this.PERSON_ID_KEY);

        if (currentToken) this.storage.setItem(this.TOKEN_KEY, currentToken);
        if (currentRefreshToken) this.storage.setItem(this.REFRESH_TOKEN_KEY, currentRefreshToken);
        if (currentExpiration) this.storage.setItem(this.EXPIRATION_KEY, currentExpiration);
        if (currentPersonId) this.storage.setItem(this.PERSON_ID_KEY, currentPersonId);

        oldStorage.removeItem(this.TOKEN_KEY);
        oldStorage.removeItem(this.REFRESH_TOKEN_KEY);
        oldStorage.removeItem(this.EXPIRATION_KEY);
        oldStorage.removeItem(this.PERSON_ID_KEY);
      }
    }
  }

  get rememberMe(): boolean {
    return this._rememberMe;
  }

  private storeAuthData(accessToken: string, refreshToken: string, expiresIn: number, personId: number): void {
    this.token = accessToken;
    this.storedRefreshToken = refreshToken;
    this.tokenExpiration = Math.floor(Date.now() / 1000) + expiresIn;
    this.personId = personId;
    this.userLogin.next(true);
  }

  private decodeAndSetClaims(token: string): void {
    try {
      const payloadParts = token.split('.');
      if (payloadParts.length !== 3) {
        throw new Error('Invalid JWT token format.');
      }
      let payload = payloadParts[1];

      payload = payload.replace(/-/g, '+').replace(/_/g, '/');
      const padding = '='.repeat((4 - payload.length % 4) % 4);
      const base64 = payload + padding;

      const decodedPayload = JSON.parse(atob(base64));

      const canCure = !!decodedPayload['CanManageCuration'];
      const canManageRoles = !!decodedPayload['CanManageUserRoles'];

      this._canManageCuration.next(canCure);
      this._canManageUserRoles.next(canManageRoles);
    } catch (error) {
      console.error("Failed to decode token or claims:", error);
      this.clearClaims();
    }
  }

  private clearClaims(): void {
    this._canManageCuration.next(false);
    this._canManageUserRoles.next(false);
  }

  checkUserLoggedInStatus(): void {
    const isLoggedIn = !!this.token;
    if (isLoggedIn && this.token) {
      this.decodeAndSetClaims(this.token);
    } else {
      this.clearClaims();
    }
    if (this.userLogin.value !== isLoggedIn) {
      this.userLogin.next(isLoggedIn);
    }
  }

  hasAccessToken(): boolean {
    return !!this.token;
  }

  login(credentials: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.apiUrl}/login`, credentials)
      .pipe(
        tap((response) => {
          this.storeAuthData(
            response.accessToken,
            response.refreshToken,
            response.expiresIn,
            response.personId
          );
          this.notificationService.success('Logged in successfully!');
        }),
        catchError((error) => {
          this.notificationService.error(
            'Login failed: ' +
            (error.error?.message || 'Please check your credentials.')
          );
          return throwError(() => error);
        })
      );
  }

  logout(): void {
    this.storage.removeItem(this.TOKEN_KEY);
    this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    this.storage.removeItem(this.EXPIRATION_KEY);
    this.storage.removeItem(this.PERSON_ID_KEY);
    localStorage.removeItem(this.REMEMBER_ME_KEY);

    this._accessToken = undefined;
    this._refreshToken = undefined;
    this._tokenExpiration = undefined;
    this._personId = undefined;
    this._rememberMe = false;
    this.storage = sessionStorage;

    this.userLogin.next(false);
    this.clearClaims();
    this.router.navigate(['/home']);
    this.notificationService.info('You have been logged out.');
  }

  refreshToken(): Observable<string> {
    if (this.isRefreshing) {
      return this.refreshTokenSubject.asObservable().pipe(
        filter((token) => token !== null),
        take(1),
        switchMap((token: string) => {
          return token
            ? of(token)
            : throwError(() => new Error('Refresh token failed while already refreshing.'));
        })
      );
    }

    this.isRefreshing = true;
    this.refreshTokenSubject.next(null);

    const refreshToken = this.storedRefreshToken;
    if (!refreshToken) {
      this.logout();
      return throwError(() => new Error('No refresh token available.'));
    }

    return this.http
      .post<RefreshTokenResponse>(`${this.apiUrl}/refresh`, {
        refreshToken,
      })
      .pipe(
        tap((response) => {
          this.storeAuthData(
            response.accessToken,
            response.refreshToken,
            response.expiresIn,
            this.personId || 0
          );
        }),
        switchMap((response) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.next(response.accessToken);
          return of(response.accessToken);
        }),
        catchError((error) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.error(error);
          this.logout();
          this.notificationService.error('Session expired. Please log in again.');
          return throwError(() => error);
        }),
        finalize(() => {
          this.isRefreshing = false;
        })
      );
  }
}