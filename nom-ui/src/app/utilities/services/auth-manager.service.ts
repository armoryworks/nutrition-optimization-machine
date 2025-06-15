import { Injectable } from '@angular/core';
import { BehaviorSubject, Subject } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class AuthManagerService {
  // Public observable for login status changes.
  // BehaviorSubject ensures new subscribers get the current status immediately.
  public userLogin: BehaviorSubject<boolean> = new BehaviorSubject<boolean>(
    false
  );

  // Subject to signal that the user menu should be opened (e.g., after registration).
  public openUserMenuSignal: Subject<void> = new Subject<void>();

  // Private backing fields for token and rememberMe state.
  private _accessToken?: string;
  private _refreshToken?: string;
  private _tokenExpiration?: number;
  private _rememberMe: boolean = false; // Tracks the "remember me" preference

  // Dynamically selected storage (localStorage or sessionStorage) based on rememberMe.
  private storage!: Storage;

  // Keys used for storing authentication data in browser storage.
  private readonly TOKEN_KEY = 'nom-token';
  private readonly REFRESH_TOKEN_KEY = 'nom-refresh-token';
  private readonly EXPIRATION_KEY = 'nom-token-expiration';
  private readonly REMEMBER_ME_KEY = 'nom-remember-me';

  constructor() {
    // Initialize storage based on the last remembered preference
    // This is crucial to select the correct storage type before token access.
    this._rememberMe = localStorage.getItem(this.REMEMBER_ME_KEY) === 'true';
    this.storage = this._rememberMe ? localStorage : sessionStorage;

    // Load existing tokens from the determined storage
    this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    const storedExpiration = this.storage.getItem(this.EXPIRATION_KEY);
    this._tokenExpiration = storedExpiration
      ? parseInt(storedExpiration, 10)
      : undefined;
    this._refreshToken =
      this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;

    // Immediately check and set initial login status
    this.checkUserLoggedInStatus();
  }

  // Getter/Setter for Access Token
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
    // If _accessToken is null/undefined, try to load from current storage
    if (!this._accessToken) {
      this._accessToken = this.storage.getItem(this.TOKEN_KEY) || undefined;
    }
    return this._accessToken;
  }

  // Getter/Setter for Refresh Token
  set refreshToken(value: string | undefined) {
    this._refreshToken = value;
    if (this._refreshToken) {
      this.storage.setItem(this.REFRESH_TOKEN_KEY, this._refreshToken);
    } else {
      this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    }
  }

  get refreshToken(): string | undefined {
    if (!this._refreshToken) {
      this._refreshToken =
        this.storage.getItem(this.REFRESH_TOKEN_KEY) || undefined;
    }
    return this._refreshToken;
  }

  // Getter/Setter for Token Expiration
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

  // Getter/Setter for Remember Me preference
  // NOTE: This setter is crucial as it determines which storage (localStorage/sessionStorage) is used.
  set rememberMe(value: boolean) {
    if (this._rememberMe !== value) {
      this._rememberMe = value;
      localStorage.setItem(this.REMEMBER_ME_KEY, value.toString()); // Always store preference in localStorage

      // IMPORTANT: If storage type changes, move current tokens
      const oldStorage = this.storage; // Capture current storage
      this.storage = this._rememberMe ? localStorage : sessionStorage; // Set new storage

      // If storage type changed, copy data from old storage to new one before clearing old
      // This handles switching from session to local or vice-versa gracefully during a session.
      if (oldStorage !== this.storage) {
        const currentToken = oldStorage.getItem(this.TOKEN_KEY);
        const currentRefreshToken = oldStorage.getItem(this.REFRESH_TOKEN_KEY);
        const currentExpiration = oldStorage.getItem(this.EXPIRATION_KEY);

        if (currentToken) this.storage.setItem(this.TOKEN_KEY, currentToken);
        if (currentRefreshToken)
          this.storage.setItem(this.REFRESH_TOKEN_KEY, currentRefreshToken);
        if (currentExpiration)
          this.storage.setItem(this.EXPIRATION_KEY, currentExpiration);

        // Clear from old storage after moving
        oldStorage.removeItem(this.TOKEN_KEY);
        oldStorage.removeItem(this.REFRESH_TOKEN_KEY);
        oldStorage.removeItem(this.EXPIRATION_KEY);
      }
    }
  }

  get rememberMe(): boolean {
    return this._rememberMe;
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
   * Clears all authentication-related data from the currently used storage after logout.
   */
  clearStorageAfterLogout(): void {
    this.storage.removeItem(this.TOKEN_KEY);
    this.storage.removeItem(this.REFRESH_TOKEN_KEY);
    this.storage.removeItem(this.EXPIRATION_KEY);
    // Also explicitly clear the rememberMe preference from localStorage
    localStorage.removeItem(this.REMEMBER_ME_KEY);

    // Reset internal state
    this._accessToken = undefined;
    this._refreshToken = undefined;
    this._tokenExpiration = undefined;
    this._rememberMe = false; // Reset the preference

    // After logout, default to sessionStorage for subsequent operations until rememberMe is set again
    this.storage = sessionStorage;

    this.userLogin.next(false); // Ensure login status is updated
  }
}
