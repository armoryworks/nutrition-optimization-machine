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

import { NotificationService } from '../../utilities/services/notification.service'; // Adjust path if necessary

// Define interfaces for API responses/requests
interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number; // Time in seconds until access token expires
  personId: number; // The actual person ID associated with the user
  // Add other user details if needed
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
  // Public observable for login status changes.
  public userLogin: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(
    false
  );

  // Subject to signal that the user menu should be opened (e.g., after registration).
  public openUserMenuSignal: Subject<void> = new Subject<void>();

  // Backend API URL for authentication endpoints
  private apiUrl = 'YOUR_BACKEND_AUTH_API_URL'; // TODO: IMPORTANT: Replace with your actual backend authentication API URL (e.g., 'https://localhost:5001/api/auth')

  // Internal flags for token refreshing
  private isRefreshing = false; // Flag to prevent multiple refresh calls simultaneously
  private refreshTokenSubject: BehaviorSubject<any> = new BehaviorSubject<any>(
    null
  ); // Used to queue requests during token refresh

  // Keys used for storing authentication data in browser storage.
  private readonly TOKEN_KEY = 'nom-token';
  private readonly REFRESH_TOKEN_KEY = 'nom-refresh-token';
  private readonly EXPIRATION_KEY = 'nom-token-expiration';
  private readonly REMEMBER_ME_KEY = 'nom-remember-me';
  private readonly PERSON_ID_KEY = 'nom-person-id'; // Key for storing the primary PersonId

  // Private backing fields for token and rememberMe state.
  private _accessToken?: string;
  private _refreshToken?: string; // This is the backing field for the stored refresh token
  private _tokenExpiration?: number;
  private _rememberMe: boolean = false; // Tracks the "remember me" preference
  private _personId?: number; // Stores the primary PersonId

  // Dynamically selected storage (localStorage or sessionStorage) based on rememberMe.
  private storage!: Storage;

  constructor(
    private http: HttpClient,
    private router: Router,
    private notificationService: NotificationService
  ) {
    // Initialize storage based on the last remembered preference
    this._rememberMe = localStorage.getItem(this.REMEMBER_ME_KEY) === 'true';
    this.storage = this._rememberMe ? localStorage : sessionStorage;

    // Load existing tokens and personId from the determined storage
    this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    const storedExpiration = this.storage.getItem(this.EXPIRATION_KEY);
    this._tokenExpiration = storedExpiration
      ? parseInt(storedExpiration, 10)
      : undefined;
    this._refreshToken =
      this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;
    const storedPersonId = this.storage.getItem(this.PERSON_ID_KEY);
    this._personId = storedPersonId ? parseInt(storedPersonId, 10) : undefined;

    // Immediately check and set initial login status
    this.checkUserLoggedInStatus();
  }

  // --- Getters/Setters for Auth Data ---

  set token(value: string | undefined) {
    this._accessToken = value;
    if (this._accessToken) {
      this.storage.setItem(this.TOKEN_KEY, this._accessToken);
    } else {
      this.storage.removeItem(this.TOKEN_KEY);
    }
    // Update login status whenever token changes
    this.userLogin.next(!!this._accessToken);
  }

  get token(): string | undefined {
    if (!this._accessToken) {
      this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    }
    return this._accessToken;
  }

  // RENAMED: Getter for the stored refresh token
  set storedRefreshToken(value: string | undefined) {
    this._refreshToken = value;
    if (this._refreshToken) {
      this.storage.setItem(this.REFRESH_TOKEN_KEY, this._refreshToken);
    } else {
      this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    }
  }

  // RENAMED: Getter for the stored refresh token
  get storedRefreshToken(): string | undefined {
    if (!this._refreshToken) {
      this._refreshToken =
        this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;
    }
    return this._refreshToken;
  }

  set tokenExpiration(value: number | undefined) {
    this._tokenExpiration = value;
    if (this._tokenExpiration) {
      this.storage.setItem(
        this.EXPIRATION_KEY,
        this._tokenExpiration.toString()
      );
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

  // Getter/Setter for Remember Me preference
  set rememberMe(value: boolean) {
    if (this._rememberMe !== value) {
      this._rememberMe = value;
      localStorage.setItem(this.REMEMBER_ME_KEY, value.toString()); // Always store preference in localStorage

      // IMPORTANT: If storage type changes, move current tokens
      const oldStorage = this.storage; // Capture current storage
      this.storage = this._rememberMe ? localStorage : sessionStorage; // Set new storage

      // If storage type changed, copy data from old storage to new one before clearing old
      if (oldStorage !== this.storage) {
        const currentToken = oldStorage.getItem(this.TOKEN_KEY);
        const currentRefreshToken = oldStorage.getItem(this.REFRESH_TOKEN_KEY);
        const currentExpiration = oldStorage.getItem(this.EXPIRATION_KEY);
        const currentPersonId = oldStorage.getItem(this.PERSON_ID_KEY);

        if (currentToken) this.storage.setItem(this.TOKEN_KEY, currentToken);
        if (currentRefreshToken)
          this.storage.setItem(this.REFRESH_TOKEN_KEY, currentRefreshToken);
        if (currentExpiration)
          this.storage.setItem(this.EXPIRATION_KEY, currentExpiration);
        if (currentPersonId)
          this.storage.setItem(this.PERSON_ID_KEY, currentPersonId);

        // Clear from old storage after moving
        oldStorage.removeItem(this.TOKEN_KEY);
        oldStorage.removeItem(this.REFRESH_TOKEN_KEY);
        oldStorage.removeItem(this.EXPIRATION_KEY);
        oldStorage.removeItem(this.PERSON_ID_KEY); // Clear personId from old storage too
      }
    }
  }

  get rememberMe(): boolean {
    return this._rememberMe;
  }

  // --- Auth Logic Methods ---

  /**
   * Helper to store all authentication tokens and related data.
   */
  private storeAuthData(
    accessToken: string,
    refreshToken: string,
    expiresIn: number,
    personId: number
  ): void {
    this.token = accessToken;
    this.storedRefreshToken = refreshToken; // Use the renamed setter here
    // Calculate actual expiration time (e.g., Unix timestamp)
    this.tokenExpiration = Math.floor(Date.now() / 1000) + expiresIn;
    this.personId = personId;
    this.userLogin.next(true); // Update auth status
  }

  /**
   * Checks if a user is currently logged in based on the presence of an access token.
   * Updates the userLogin BehaviorSubject accordingly.
   */
  checkUserLoggedInStatus(): void {
    const isLoggedIn = !!this.token; // Access the getter to check token existence
    if (this.userLogin.value !== isLoggedIn) {
      this.userLogin.next(isLoggedIn);
    }
  }

  /**
   * Returns true if an access token exists (does not check for expiration).
   * This is primarily used by the HttpInterceptor.
   */
  hasAccessToken(): boolean {
    return !!this.token;
  }

  /**
   * Handles user login.
   * @param credentials LoginRequest containing email and password.
   * @returns Observable of LoginResponse.
   */
  login(credentials: LoginRequest): Observable<LoginResponse> {
    // TODO: Update with your actual login endpoint
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

  /**
   * Clears all authentication-related data and logs the user out.
   * Also redirects the user to the 'home' route.
   */
  logout(): void {
    this.storage.removeItem(this.TOKEN_KEY);
    this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    this.storage.removeItem(this.EXPIRATION_KEY);
    this.storage.removeItem(this.PERSON_ID_KEY); // Clear personId on logout

    // Also explicitly clear the rememberMe preference from localStorage
    localStorage.removeItem(this.REMEMBER_ME_KEY);

    // Reset internal state
    this._accessToken = undefined;
    this._refreshToken = undefined; // Reset the backing field
    this._tokenExpiration = undefined;
    this._personId = undefined; // Reset personId
    this._rememberMe = false; // Reset the preference

    // After logout, default to sessionStorage for subsequent operations until rememberMe is set again
    this.storage = sessionStorage;

    this.userLogin.next(false); // Ensure login status is updated
    this.router.navigate(['/home']); // Redirect to home route
    this.notificationService.info('You have been logged out.');
  }

  /**
   * Attempts to refresh the access token using the refresh token.
   * This method is called by the HttpInterceptor when a 401 is received.
   * @returns Observable of the new access token.
   */
  refreshToken(): Observable<string> {
    if (this.isRefreshing) {
      // If a refresh request is already in progress, wait for it to complete
      return this.refreshTokenSubject.asObservable().pipe(
        filter((token) => token !== null), // Wait until token is available
        take(1), // Take the first emitted token
        switchMap((token: string) => {
          return token
            ? of(token)
            : throwError(
                () =>
                  new Error('Refresh token failed while already refreshing.')
              );
        })
      );
    }

    this.isRefreshing = true;
    this.refreshTokenSubject.next(null); // Clear previous token on new refresh attempt

    const refreshToken = this.storedRefreshToken; // Use the renamed getter for the stored refresh token
    if (!refreshToken) {
      this.logout(); // No refresh token, force logout
      return throwError(() => new Error('No refresh token available.'));
    }

    // TODO: Update with your actual refresh token endpoint
    // The backend should receive the refresh token and return a new access token & refresh token
    return this.http
      .post<RefreshTokenResponse>(`${this.apiUrl}/refresh-token`, {
        refreshToken, // Pass the stored refresh token to the backend
      })
      .pipe(
        tap((response) => {
          // Update tokens and personId in storage
          this.storeAuthData(
            response.accessToken,
            response.refreshToken,
            response.expiresIn,
            this.personId || 0 // Use existing personId, or default if somehow missing
          );
        }),
        switchMap((response) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.next(response.accessToken); // Emit the new access token
          return of(response.accessToken); // Return the new access token
        }),
        catchError((error) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.error(error); // Notify subscribers of refresh failure
          this.logout(); // Refresh failed, force logout
          this.notificationService.error(
            'Session expired. Please log in again.'
          );
          return throwError(() => error);
        }),
        finalize(() => {
          this.isRefreshing = false; // Ensure flag is reset regardless of success/failure
        })
      );
  }
}
