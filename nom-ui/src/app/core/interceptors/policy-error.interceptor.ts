import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';

/**
 * Surfaces household-policy rejections from nom-api as friendly snackbars:
 *
 * - 4xx `{ reason: "restriction_violation:…" }` from plan-slot saves and
 *   recipe edits that conflict with a locked dietary restriction;
 * - 403 `{ reason: "feature_gated:<key>" }` when a gated feature is invoked;
 *
 * `steward_required` is intentionally not handled here — steward screens
 * handle it inline with context-appropriate messaging. The error is always
 * rethrown so callers can still run their own handling.
 */
export const policyErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const snackBar = inject(MatSnackBar);

  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        // Controllers use {message, reason}; anything escaping through the
        // global exception middleware carries the reason in ProblemDetails.detail.
        const reason: unknown = err.error?.reason ?? err.error?.detail;
        if (typeof reason === 'string') {
          if (reason.startsWith('restriction_violation')) {
            snackBar.open(
              'This recipe conflicts with a locked dietary restriction in your household.',
              'OK',
              { duration: 6000 },
            );
          } else if (reason.startsWith('feature_gated')) {
            snackBar.open('This feature is disabled by your household policy.', 'OK', {
              duration: 6000,
            });
          }
        }
      }
      return throwError(() => err);
    }),
  );
};
