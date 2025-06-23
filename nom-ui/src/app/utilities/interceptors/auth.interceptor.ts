import { Injectable } from '@angular/core';
import {
  HttpRequest,
  HttpHandler,
  HttpEvent,
  HttpInterceptor,
  HttpErrorResponse,
} from '@angular/common/http';
import { Observable, throwError, BehaviorSubject } from 'rxjs';
import { catchError, switchMap, filter, take, tap } from 'rxjs/operators';
import { AuthManagerService } from '../services/auth-manager.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  private isRefreshing = false;
  private refreshTokenSubject: BehaviorSubject<any> = new BehaviorSubject<any>(
    null
  );

  constructor(private authManager: AuthManagerService) {}

  intercept(
    request: HttpRequest<any>,
    next: HttpHandler
  ): Observable<HttpEvent<any>> {
    // 1. Add access token to outgoing requests
    const accessToken = this.authManager.token;
    if (accessToken) {
      request = this.addToken(request, accessToken);
    }

    // 2. Handle incoming responses
    return next.handle(request).pipe(
      catchError((error) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          // Check if it's an API request that requires authentication
          // You might have specific patterns for authenticated API routes
          // e.g., if (!request.url.includes('/api/auth/')) { ... }
          return this.handle401Error(request, next, error);
        } else {
          // For other errors, just re-throw
          return throwError(() => error);
        }
      })
    );
  }

  private addToken(request: HttpRequest<any>, token: string): HttpRequest<any> {
    return request.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  private handle401Error(
    request: HttpRequest<any>,
    next: HttpHandler,
    error: HttpErrorResponse
  ): Observable<HttpEvent<any>> {
    if (this.isRefreshing) {
      // If token refresh is already in progress, queue this request
      return this.refreshTokenSubject.pipe(
        filter((token) => token !== null), // Wait until token is available
        take(1), // Take the first emitted token
        switchMap((token) => next.handle(this.addToken(request, token))) // Retry original request with new token
      );
    } else {
      // Start refreshing the token
      this.isRefreshing = true;
      this.refreshTokenSubject.next(null); // Clear previous token
      console.warn('401 Unauthorized: Attempting to refresh token...');

      return this.authManager.refreshToken().pipe(
        switchMap((newToken: string) => {
          this.isRefreshing = false;
          this.refreshTokenSubject.next(newToken); // Emit new token to queued requests
          return next.handle(this.addToken(request, newToken)); // Retry original request with new token
        }),
        catchError((refreshError: any) => {
          this.isRefreshing = false;
          // Refresh token failed or no refresh token, logout the user
          console.error('Token refresh failed. Logging out...', refreshError);
          this.authManager.logout(); // Calls logout and redirects to /home
          return throwError(() => refreshError); // Re-throw the refresh error
        })
      );
    }
  }
}
