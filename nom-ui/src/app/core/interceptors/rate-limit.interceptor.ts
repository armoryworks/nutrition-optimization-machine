import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Observable, catchError, switchMap, throwError, timer } from 'rxjs';

const MAX_AUTO_RETRY_SECONDS = 10;

/**
 * Handles 429 Too Many Requests from nom-api. A throttled GET is retried once
 * after the server's Retry-After when that wait is short; anything still
 * throttled (or not safe to repeat) surfaces a snackbar telling the user to
 * slow down. The error is always rethrown so callers can still run their own
 * handling.
 */
export const rateLimitInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  const notify = (err: unknown): Observable<never> => {
    if (err instanceof HttpErrorResponse && err.status === 429) {
      snackBar.open("You're going a bit fast. Wait a few seconds and try again.", 'OK', {
        duration: 6000,
      });
    }
    return throwError(() => err);
  };

  return next(req).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 429 || req.method !== 'GET') {
        return notify(err);
      }
      const retryAfter = Number(err.headers.get('Retry-After'));
      const seconds = Number.isFinite(retryAfter) && retryAfter > 0 ? retryAfter : 1;
      if (seconds > MAX_AUTO_RETRY_SECONDS) {
        return notify(err);
      }
      return timer(seconds * 1000).pipe(
        switchMap(() => next(req)),
        catchError(notify),
      );
    }),
  );
};
