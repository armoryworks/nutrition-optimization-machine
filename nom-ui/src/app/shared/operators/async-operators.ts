import { WritableSignal } from '@angular/core';
import { Observable, pipe, EMPTY } from 'rxjs';
import { tap, catchError, finalize } from 'rxjs/operators';

/**
 * RxJS operator that automatically manages a loading signal.
 * Sets loading to true on subscribe, false on complete/error.
 *
 * Usage:
 *   this.service.getData().pipe(
 *     withLoading(this.isLoading)
 *   ).subscribe(...)
 */
export function withLoading<T>(loadingSignal: WritableSignal<boolean>) {
  return (source: Observable<T>): Observable<T> => {
    loadingSignal.set(true);
    return source.pipe(
      finalize(() => loadingSignal.set(false))
    );
  };
}

/**
 * RxJS operator that automatically manages an error signal.
 * Clears error on subscribe, sets it on error.
 *
 * Usage:
 *   this.service.getData().pipe(
 *     withError(this.error, 'Failed to load data.')
 *   ).subscribe(...)
 */
export function withError<T>(
  errorSignal: WritableSignal<string | null>,
  fallbackMessage: string
) {
  return (source: Observable<T>): Observable<T> => {
    errorSignal.set(null);
    return source.pipe(
      catchError((err) => {
        const message = err?.message || fallbackMessage;
        errorSignal.set(message);
        console.error(fallbackMessage, err);
        return EMPTY;
      })
    );
  };
}

export interface AsyncStateOptions {
  /** Fallback error message if the error has no message */
  errorMessage: string;
  /** If true, the error is swallowed (observable completes). Defaults to true. */
  swallowError?: boolean;
}

/**
 * Combined operator that manages both loading and error signals.
 * This replaces the common pattern:
 *   this.isLoading.set(true);
 *   this.error.set(null);
 *   this.service.getData().pipe(
 *     finalize(() => this.isLoading.set(false))
 *   ).subscribe({
 *     error: (err) => this.error.set('...')
 *   })
 *
 * Usage:
 *   this.service.getData().pipe(
 *     withAsyncState(this.isLoading, this.error, {
 *       errorMessage: ERROR_MESSAGES.RECIPE.LOAD_FAILED
 *     })
 *   ).subscribe(data => this.data.set(data))
 */
export function withAsyncState<T>(
  loadingSignal: WritableSignal<boolean>,
  errorSignal: WritableSignal<string | null>,
  options: AsyncStateOptions
) {
  const { errorMessage, swallowError = true } = options;

  return (source: Observable<T>): Observable<T> => {
    loadingSignal.set(true);
    errorSignal.set(null);

    return source.pipe(
      catchError((err) => {
        const message = err?.message || errorMessage;
        errorSignal.set(message);
        console.error(errorMessage, err);
        if (swallowError) {
          return EMPTY;
        }
        throw err;
      }),
      finalize(() => loadingSignal.set(false))
    );
  };
}
