import { HttpClient } from '@angular/common/http';
import { Injectable, Injector } from '@angular/core';
import { ReplaySubject, throwError } from 'rxjs'; // Added 'throwError'
import { catchError, take } from 'rxjs/operators'; // Added 'catchError'
import { NomConfig } from '../models/nom-config';

/**
 * Service for managing application configuration settings.
 * Provides methods to load settings from external JSON files and access the configuration object.
 */
@Injectable({
  providedIn: 'root',
})
export class NomConfigService {
  /**
   * Holds the application configuration object.
   */
  private _config: NomConfig;

  /**
   * URI for the development configuration file.
   */
  readonly DEFAULT_DEV_URI = '/assets/nom-config.development.json';

  /**
   * URI for the production configuration file.
   */
  readonly DEFAULT_URI = '/assets/nom-config.json';

  /**
   * ReplaySubject to notify subscribers when settings are loaded.
   */
  settingsLoaded: ReplaySubject<void> = new ReplaySubject<void>(1);

  /**
   * Constructor for NomConfigService.
   * @param injector - Angular injector for dynamically resolving dependencies.
   */
  constructor(private injector: Injector) {
    this._config = new NomConfig();
  }

  /**
   * Loads configuration settings from external JSON files.
   * Tries the development URI first, then falls back to the production URI if the development file is not found.
   * @returns A promise that resolves when settings are successfully loaded or rejects on error.
   */
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

  /**
   * Getter for the configuration object.
   * @returns The loaded configuration object.
   */
  get config(): NomConfig {
    return this._config;
  }
}
