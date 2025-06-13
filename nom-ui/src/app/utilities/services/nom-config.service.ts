import { HttpClient } from '@angular/common/http';
import { Injectable, Injector } from '@angular/core';
import { ReplaySubject, throwError } from 'rxjs'; // Added 'throwError'
import { catchError, take } from 'rxjs/operators'; // Added 'catchError'
import { NomConfig } from '../models/nom-config';

@Injectable({
  providedIn: 'root',
})
export class NomConfigService {
  private _config: NomConfig;

  get config(): NomConfig {
    return this._config;
  }

  readonly DEFAULT_DEV_URI = '/assets/nom-config.development.json';
  readonly DEFAULT_URI = '/assets/nom-config.json';

  settingsLoaded: ReplaySubject<void> = new ReplaySubject<void>(1);

  constructor(private injector: Injector) {
    this._config = new NomConfig();
  }

  loadSettings(): Promise<void> {
    const httpClient = this.injector.get(HttpClient);

    return new Promise((resolve, reject) => {
      httpClient
        .get<NomConfig>(this.DEFAULT_DEV_URI)
        .pipe(
          take(1),
          catchError((error) => {
            if (error.status === 404) {
              return httpClient.get<NomConfig>(this.DEFAULT_URI).pipe(
                take(1),
                catchError((fallbackError) => {
                  return throwError(() => fallbackError);
                })
              );
            } else {
              return throwError(() => error);
            }
          })
        )
        .subscribe({
          next: (response) => {
            this._config = response;
            this.settingsLoaded.next();
            resolve();
          },
          error: (error) => {
            this.settingsLoaded.error(error);
            reject(error);
          },
        });
    });
  }
}
