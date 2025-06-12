import { Injectable } from '@angular/core';
import { ReplaySubject } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class AuthManagerService {
  userLogin: ReplaySubject<void> = new ReplaySubject<void>(1);

  private storage: Storage;

  private _token?: string = undefined;
  private _rememberMe: boolean = false;
  public get rememberMe(): boolean {
    return this._rememberMe;
  }
  //NOTE: THIS VALUE MUST BE SET PRIOR TO SETTING TOKEN AFTER LOGIN
  public set rememberMe(value: boolean) {
    if (this._rememberMe !== value) {
      this._rememberMe = value;
      this.storage = this._rememberMe ? localStorage : sessionStorage;
    }
  }

  get token(): string | undefined {
    this.fireLoggedIn();
    return this._token;
  }
  set token(value: string | undefined) {
    this.storage['nom-token'] = value;
    this.fireLoggedIn();
  }

  get tokenExpiration(): number | undefined {
    return this.storage['nom-token-expiration'];
  }
  set tokenExpiration(value: number | undefined) {
    this.storage['nom-token-expiration'] = value;
  }

  get refreshToken(): string | undefined {
    return this.storage['nom-refresh-token'];
  }
  set refreshToken(value: string | undefined) {
    this.storage['nom-refresh-token'] = value;
  }

  constructor() {
    this.storage = sessionStorage;
    this.rememberMe = !!localStorage['nom-token'];
  }

  public clearStorageAfterLogout() {
    this.storage.removeItem('nom-token');
    this.storage.removeItem('nom-refresh-token');
    this.storage.removeItem('nom-token-expiration');
  }

  public checkUserLoggedInStatus(): boolean {
    return !!this.token;
  }

  private fireLoggedIn() {
    const doFireLoggedIn =
      (this._token && !this.storage['nom-token']) ||
      this._token !== this.storage['nom-token'];
    if (doFireLoggedIn) {
      this._token = this.storage['nom-token'];
      setTimeout(() => {
        this.userLogin.next();
      });
    }
  }
}
