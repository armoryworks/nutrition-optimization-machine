import { Routes } from '@angular/router';

export const PANTRY_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./pantry.component').then(m => m.PantryComponent),
    title: 'Pantry',
  },
];
