// File: nom-ui/src/app/guards/auth.guard.ts

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs/operators';
import { Observable, of } from 'rxjs';

/**
 * A functional route guard that checks if a user is authenticated.
 * If the user is logged in, it allows access to the route.
 * If the user is not logged in, it redirects them to the home/login page.
 * @returns An Observable<boolean> or a boolean indicating if activation is allowed.
 */
export const AuthGuard: CanActivateFn = (): Observable<boolean> => {
  const router = inject(Router);

  // Check for the same token key that AuthService uses
  const token = localStorage.getItem('authToken');
  const isLoggedIn = !!token;

  if (isLoggedIn) {
    return of(true); // User is logged in, allow access
  }

  // User is not logged in, redirect to the home page (which contains the login component)
  router.navigate(['/home']);
  return of(false); // Block access
};