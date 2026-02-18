import { HttpInterceptorFn, HttpRequest, HttpHandlerFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from '../services/auth.service';

let isRefreshing = false;

export const authInterceptor: HttpInterceptorFn = (req: HttpRequest<unknown>, next: HttpHandlerFn) => {
  const authService = inject(AuthService);

  // Don't attach token to auth endpoints (login, register, refresh, etc.)
  // except for /manage/info which requires auth
  if (isAuthEndpoint(req.url) && !req.url.includes('/manage/')) {
    return next(req);
  }

  const token = authService.accessToken;
  const authedReq = token ? addToken(req, token) : req;

  return next(authedReq).pipe(
    catchError((error) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && token && !isRefreshing) {
        isRefreshing = true;
        return authService.attemptTokenRefresh().pipe(
          switchMap((result) => {
            isRefreshing = false;
            if (result) {
              return next(addToken(req, result.accessToken));
            }
            return throwError(() => error);
          }),
          catchError((refreshError) => {
            isRefreshing = false;
            return throwError(() => refreshError);
          })
        );
      }
      return throwError(() => error);
    })
  );
};

function addToken(req: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });
}

function isAuthEndpoint(url: string): boolean {
  return url.includes('/api/auth/');
}
