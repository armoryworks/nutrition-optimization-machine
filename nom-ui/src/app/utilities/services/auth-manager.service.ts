// File: nom-ui/src/app/utilities/services/auth-manager.service.ts

import { Injectable, inject, signal } from '@angular/core';
import { Subject, Observable, throwError, of } from 'rxjs';
import { Router } from '@angular/router';
import {
  tap,
  catchError,
  switchMap,
  filter,
} from 'rxjs/operators';
import { NotificationService } from '../../utilities/services/notification.service';
import { EventBusService } from './event-bus.service';
import { UserInfoService } from './user-info.service';
import { AuthService } from '../../auth/auth.service';
import { LoginResponse } from '../../auth/models/login-response';
import { LoginUser } from '../../auth/models/login-user';

@Injectable({
  providedIn: 'root',
})
export class AuthManagerService {
  private router = inject(Router);
  private notificationService = inject(NotificationService);
  private eventBus = inject(EventBusService);
  private userInfoService = inject(UserInfoService);
  private authService = inject(AuthService);

  public userLogin = signal<boolean>(false);
  public openUserMenuSignal: Subject<void> = new Subject<void>();

  // NEW: Signals for administrative roles
  public canManageCuration = signal<boolean>(false);
  public canManageUserRoles = signal<boolean>(false);

  private readonly TOKEN_KEY = 'nom-token';
  private readonly REFRESH_TOKEN_KEY = 'nom-refresh-token';
  private readonly EXPIRATION_KEY = 'nom-token-expiration';
  private readonly REMEMBER_ME_KEY = 'nom-remember-me';
  private readonly PERSON_ID_KEY = 'nom-person-id';

  private _accessToken?: string;
  private _refreshToken?: string;
  private _tokenExpiration?: number;
  private _rememberMe = false;
  private _personId?: number;
  private storage!: Storage;



  constructor() {
    this._rememberMe = localStorage.getItem(this.REMEMBER_ME_KEY) === 'true';
    this.storage = this._rememberMe ? localStorage : sessionStorage;

    // Check both storage locations for tokens
    this._accessToken = this.storage.getItem(this.TOKEN_KEY) ||
      (this.storage === localStorage ? sessionStorage.getItem(this.TOKEN_KEY) : localStorage.getItem(this.TOKEN_KEY)) ||
      undefined;

    const storedExpiration = this.storage.getItem(this.EXPIRATION_KEY) ||
      (this.storage === localStorage ? sessionStorage.getItem(this.EXPIRATION_KEY) : localStorage.getItem(this.EXPIRATION_KEY));
    this._tokenExpiration = storedExpiration ? parseInt(storedExpiration, 10) : undefined;

    this._refreshToken = this.storage.getItem(this.REFRESH_TOKEN_KEY) ||
      (this.storage === localStorage ? sessionStorage.getItem(this.REFRESH_TOKEN_KEY) : localStorage.getItem(this.REFRESH_TOKEN_KEY)) ||
      undefined;

    const storedPersonId = this.storage.getItem(this.PERSON_ID_KEY) ||
      (this.storage === localStorage ? sessionStorage.getItem(this.PERSON_ID_KEY) : localStorage.getItem(this.PERSON_ID_KEY));
    this._personId = storedPersonId ? parseInt(storedPersonId, 10) : undefined;

    // Don't call checkUserLoggedInStatus() here to avoid circular dependency
    // It will be called when needed by other components

    // Listen to user info updates
    this.eventBus.events$.pipe(
      filter(event => event.type === 'user:info-updated')
    ).subscribe((event) => {
      if (event.data) {
        this.updateClaimsFromUserInfo(event.data as { claims?: { type: string }[]; personId?: number });
      }
    });
  }

  set token(value: string | undefined) {
    console.log('Token setter called with value:', value ? 'present' : 'undefined');
    this._accessToken = value;
    if (this._accessToken) {
      this.storage.setItem(this.TOKEN_KEY, this._accessToken);
      console.log('Calling loadUserClaims from token setter...');
      this.loadUserClaims();
    } else {
      this.storage.removeItem(this.TOKEN_KEY);
      this.clearClaims();
    }
    this.userLogin.set(!!this._accessToken);
  }

  get token(): string | undefined {
    if (!this._accessToken) {
      // Check current storage first, then fallback to the other storage
      this._accessToken = this.storage.getItem(this.TOKEN_KEY) ||
        (this.storage === localStorage ? sessionStorage.getItem(this.TOKEN_KEY) : localStorage.getItem(this.TOKEN_KEY)) ||
        undefined;
    }
    return this._accessToken;
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
      // Check current storage first, then fallback to the other storage
      const stored = this.storage.getItem(this.EXPIRATION_KEY) ||
        (this.storage === localStorage ? sessionStorage.getItem(this.EXPIRATION_KEY) : localStorage.getItem(this.EXPIRATION_KEY));
      this._tokenExpiration = stored ? parseInt(stored, 10) : undefined;
    }
    return this._tokenExpiration;
  }

  set personId(value: number | undefined) {
    this._personId = value;
    if (this._personId) {
      this.storage.setItem(this.PERSON_ID_KEY, this._personId.toString());
    } else {
      this.storage.removeItem(this.PERSON_ID_KEY);
    }
  }

  get personId(): number | undefined {
    if (!this._personId) {
      // Check current storage first, then fallback to the other storage
      const stored = this.storage.getItem(this.PERSON_ID_KEY) ||
        (this.storage === localStorage ? sessionStorage.getItem(this.PERSON_ID_KEY) : localStorage.getItem(this.PERSON_ID_KEY));
      this._personId = stored ? parseInt(stored, 10) : undefined;
    }
    return this._personId;
  }

  set rememberMe(value: boolean) {
    this._rememberMe = value;
    localStorage.setItem(this.REMEMBER_ME_KEY, value.toString());
    this.storage = value ? localStorage : sessionStorage;
  }

  get rememberMe(): boolean {
    return this._rememberMe;
  }

  isLoggedIn(): boolean {
    const hasToken = !!this.token;
    const hasExpiration = !!this.tokenExpiration;
    const isNotExpired = this.tokenExpiration ? this.tokenExpiration > Date.now() : false;

    console.log('isLoggedIn check:', {
      hasToken,
      hasExpiration,
      tokenExpiration: this.tokenExpiration,
      currentTime: Date.now(),
      isNotExpired,
      result: hasToken && hasExpiration && isNotExpired
    });

    return hasToken && hasExpiration && isNotExpired;
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
      // Check current storage first, then fallback to the other storage
      this._refreshToken = this.storage.getItem(this.REFRESH_TOKEN_KEY) ||
        (this.storage === localStorage ? sessionStorage.getItem(this.REFRESH_TOKEN_KEY) : localStorage.getItem(this.REFRESH_TOKEN_KEY)) ||
        undefined;
    }
    return this._refreshToken;
  }

  private storeAuthData(accessToken: string, refreshToken: string, expiresIn: number): void {
    console.log('storeAuthData called:', {
      accessToken: accessToken ? 'present' : 'missing',
      refreshToken: refreshToken ? 'present' : 'missing',
      expiresIn,
      currentTime: Date.now(),
      calculatedExpiration: Date.now() + expiresIn
    });

    this.token = accessToken;
    this.storedRefreshToken = refreshToken;
    this.tokenExpiration = Date.now() + expiresIn;
    // personId will be retrieved from user info endpoint
  }

  private loadUserClaims(): void {
    console.log('loadUserClaims called, isLoggedIn:', this.isLoggedIn());
    if (this.isLoggedIn()) {
      // Directly load user info instead of relying on events
      console.log('Loading user info from UserInfoService...');
      this.userInfoService.getCurrentUserInfo().subscribe({
        next: (userInfo) => {
          console.log('User info received in loadUserClaims:', userInfo);
          this.updateClaimsFromUserInfo(userInfo);
        },
        error: (error) => {
          console.error('Error loading user claims:', error);
          this.clearClaims();
        }
      });
    } else {
      console.log('User not logged in, clearing claims');
      this.clearClaims();
    }
  }

  private clearClaims(): void {
    this.canManageCuration.set(false);
    this.canManageUserRoles.set(false);
  }

  private updateClaimsFromUserInfo(userInfo: { claims?: { type: string }[]; personId?: number }): void {
    console.log('Updating claims from user info:', userInfo);
    if (userInfo && userInfo.claims) {
      // Set personId from user info
      if (userInfo.personId) {
        this.personId = userInfo.personId;
      }

      const canCure = userInfo.claims.some((claim: { type: string }) => claim.type === 'CanManageCuration');
      const canManageRoles = userInfo.claims.some((claim: { type: string }) => claim.type === 'CanManageUserRoles');

      console.log('Claims found:', {
        canManageCuration: canCure,
        canManageUserRoles: canManageRoles,
        allClaims: userInfo.claims
      });

      this.canManageCuration.set(canCure);
      this.canManageUserRoles.set(canManageRoles);
    } else {
      console.log('No user info or claims found, clearing claims');
      this.clearClaims();
    }
  }

  checkUserLoggedInStatus(): void {
    const isLoggedIn = this.isLoggedIn();
    if (isLoggedIn) {
      this.loadUserClaims();
    } else {
      this.logout();
    }
    if (this.userLogin() !== isLoggedIn) {
      this.userLogin.set(isLoggedIn);
    }
  }

  hasAccessToken(): boolean {
    return !!this.token;
  }

  login(credentials: LoginUser): Observable<LoginResponse> {
    return this.authService.login(credentials).pipe(
      tap((response) => {
        console.log('Login successful, storing auth data...');
        this.storeAuthData(
          response.accessToken,
          response.refreshToken,
          response.expiresIn
        );
        this.loadUserClaims();
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
    // Call the API logout endpoint
    this.authService.logout().subscribe({
      next: () => {
        console.log('Logout API call successful');
      },
      error: (error) => {
        console.error('Logout API call failed:', error);
        // Continue with local logout even if API call fails
      },
      complete: () => {
        this.performLocalLogout();
      }
    });
  }

  private performLocalLogout(): void {
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

    this.userLogin.set(false);
    this.clearClaims();
    this.eventBus.emitLogout();
    this.router.navigate(['/home']);
    this.notificationService.info('You have been logged out.');
  }

  refreshToken(): Observable<string> {
    if (!this.storedRefreshToken) {
      return throwError(() => new Error('No refresh token available'));
    }

    return this.authService.refreshToken(this.storedRefreshToken).pipe(
      tap((response) => {
        this.storeAuthData(
          response.accessToken,
          response.refreshToken,
          response.expiresIn
        );
      }),
      switchMap((response) => of(response.accessToken)),
      catchError((error) => {
        console.error('Token refresh failed:', error);
        this.logout();
        return throwError(() => error);
      })
    );
  }
}