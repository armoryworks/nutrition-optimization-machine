import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isLoggedIn()) {
    return router.createUrlTree(['/home']);
  }

  // Trust the token if it was validated recently
  if (authService.isTokenFresh()) {
    return true;
  }

  // Otherwise validate against the backend
  return authService.validateToken().pipe(
    map(valid => valid || router.createUrlTree(['/home']))
  );
};
