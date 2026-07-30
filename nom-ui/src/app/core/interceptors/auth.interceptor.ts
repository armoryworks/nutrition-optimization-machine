import {
  HttpInterceptorFn,
  HttpRequest,
  HttpHandlerFn,
  HttpErrorResponse,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import {
  Observable,
  ReplaySubject,
  catchError,
  finalize,
  of,
  switchMap,
  take,
  throwError,
} from 'rxjs';
import { AuthService } from '../services/auth.service';
import { AuthTokenResponse } from '../models/auth-token-response.model';

/**
 * Auth endpoints that must be called WITHOUT a bearer token. Everything else —
 * including /api/auth/manage/* and /api/auth/2fa/* — gets the token attached.
 */
const UNAUTHENTICATED_AUTH_PATHS = [
  '/api/auth/login',
  '/api/auth/register',
  '/api/auth/register-custom',
  '/api/auth/refresh',
  '/api/auth/forgotPassword',
  '/api/auth/resetPassword',
  '/api/auth/confirm-email',
  '/api/auth/resend-confirmation',
];

/** Shared in-flight refresh so concurrent 401s trigger exactly one refresh call. */
let refresh$: ReplaySubject<AuthTokenResponse | null> | null = null;

export const authInterceptor: HttpInterceptorFn = (
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (isUnauthenticatedEndpoint(req.url)) {
    return next(req);
  }

  const token = authService.accessToken;
  const authedReq = token ? addToken(req, token) : req;

  return next(authedReq).pipe(
    catchError((error) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && token) {
        return refreshAndRetry(req, next, authService, router, error);
      }
      return throwError(() => error);
    }),
  );
};

function refreshAndRetry(
  req: HttpRequest<unknown>,
  next: HttpHandlerFn,
  authService: AuthService,
  router: Router,
  originalError: HttpErrorResponse,
): Observable<never> | ReturnType<HttpHandlerFn> {
  if (!refresh$) {
    const subject = new ReplaySubject<AuthTokenResponse | null>(1);
    refresh$ = subject;
    authService
      .attemptTokenRefresh()
      .pipe(
        catchError(() => of(null)),
        finalize(() => {
          refresh$ = null;
        }),
      )
      .subscribe((result) => {
        subject.next(result);
        subject.complete();
      });
  }

  return refresh$.pipe(
    take(1),
    switchMap((result) => {
      if (result) {
        return next(addToken(req, result.accessToken));
      }
      router.navigate(['/home']);
      return throwError(() => originalError);
    }),
  );
}

function addToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
}

function isUnauthenticatedEndpoint(url: string): boolean {
  const path = url.split('?')[0];
  return UNAUTHENTICATED_AUTH_PATHS.some((p) => path.endsWith(p));
}
