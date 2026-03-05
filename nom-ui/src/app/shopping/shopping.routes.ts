import { Routes } from '@angular/router';

export const SHOPPING_ROUTES: Routes = [
  {
    path: '',
    loadComponent: () => import('./shopping.component').then(m => m.ShoppingComponent),
    title: 'Shopping List',
  },
];
