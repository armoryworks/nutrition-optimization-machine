import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'home', loadComponent: () => import('./home/home.component').then(m => m.Home) },
  { path: 'recipe/:id', loadComponent: () => import('./recipe/recipe-detail.component').then(m => m.RecipeDetail) },
  { path: 'search', loadComponent: () => import('./search/search.component').then(m => m.Search) },
  { path: 'register', loadComponent: () => import('./auth/register.component').then(m => m.Register), canActivate: [guestGuard] },
  { path: 'forgot-password', loadComponent: () => import('./auth/forgot-password.component').then(m => m.ForgotPassword) },
  { path: 'reset-password', loadComponent: () => import('./auth/reset-password.component').then(m => m.ResetPassword) },

  // Protected routes
  { path: 'onboarding', loadComponent: () => import('./onboarding/onboarding.component').then(m => m.Onboarding), canActivate: [authGuard] },
  { path: 'profile', loadComponent: () => import('./profile/profile.component').then(m => m.Profile), canActivate: [authGuard] },
  { path: 'restrictions', loadComponent: () => import('./restrictions/restrictions.component').then(m => m.Restrictions), canActivate: [authGuard] },
  { path: 'household', loadComponent: () => import('./household/household.component').then(m => m.Household), canActivate: [authGuard] },
  { path: 'plan', loadComponent: () => import('./plan/plan.component').then(m => m.Plan), canActivate: [authGuard] },
  { path: 'settings', loadComponent: () => import('./settings/settings.component').then(m => m.Settings), canActivate: [authGuard] },

  { path: '**', redirectTo: 'home' },
];
