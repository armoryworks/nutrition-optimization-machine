import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'home', loadComponent: () => import('./home/home.component').then(m => m.Home) },
  { path: 'recipe/:id', loadComponent: () => import('./recipe/recipe-detail.component').then(m => m.RecipeDetail) },
  { path: 'search', loadComponent: () => import('./search/search.component').then(m => m.Search) },
  { path: 'register', loadComponent: () => import('./auth/register.component').then(m => m.Register) },
  { path: 'forgot-password', loadComponent: () => import('./auth/forgot-password.component').then(m => m.ForgotPassword) },
  { path: 'reset-password', loadComponent: () => import('./auth/reset-password.component').then(m => m.ResetPassword) },
  { path: '**', redirectTo: 'home' },
];
