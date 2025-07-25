// File: nom-ui/src/app/guards/auth.guard.ts

import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthManagerService } from '../utilities/services/auth-manager.service';
import { map } from 'rxjs/operators';
import { Observable } from 'rxjs';

/**
 * A functional route guard that checks if a user is authenticated.
 * If the user is logged in, it allows access to the route.
 * If the user is not logged in, it redirects them to the home/login page.
 * @returns An Observable<boolean> or a boolean indicating if activation is allowed.
 */
export const AuthGuard: CanActivateFn = (): Observable<boolean> => {
  const authManager = inject(AuthManagerService);
  const router = inject(Router);

  return authManager.userLogin.pipe(
    map(isLoggedIn => {
      if (isLoggedIn) {
        return true; // User is logged in, allow access
      }

      // User is not logged in, redirect to the home page (which contains the login component)
      router.navigate(['/home']);
      return false; // Block access
    })
  );
};