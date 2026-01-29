// File: nom-ui/src/app/guards/no-auth.guard.ts

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, of } from 'rxjs';

/**
 * A functional route guard that prevents authenticated users from accessing certain routes.
 * If the user is NOT logged in, it allows access (e.g., registration page).
 * If the user IS logged in, it redirects them to the onboarding workflow.
 * @returns An Observable<boolean> indicating if activation is allowed.
 */
export const NoAuthGuard: CanActivateFn = (): Observable<boolean> => {
  const router = inject(Router);

  // Check for the same token key that AuthService uses
  const token = localStorage.getItem('authToken');
  const isLoggedIn = !!token;

  if (!isLoggedIn) {
    return of(true); // User is NOT logged in, allow access to guest-only routes
  }

  // User is logged in, redirect to onboarding workflow
  router.navigate(['/onboarding']);
  return of(false); // Block access to guest-only routes
};
