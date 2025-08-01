// File: nom-ui/src/app/recipe/recipe.routes.ts

import { Routes } from '@angular/router';
import { RecipeAuthorDashboardComponent } from '../user/components/recipe-author-dashboard/recipe-author-dashboard.component';
import { RecipeEditComponent } from './components/recipe-edit/recipe-edit.component';
import { IngredientEditComponent } from './components/ingredient-edit/ingredient-edit.component';

// This defines the routes for the lazy-loaded recipe feature
export const RECIPE_ROUTES: Routes = [
  {
    path: '', // Default route for this feature (e.g., /recipes)
    component: RecipeAuthorDashboardComponent,
    title: 'My Recipes' // Optional: Set a page title
  },
  {
    path: 'search', // e.g., /recipes/search
    loadComponent: () => import('./components/recipe-search/recipe-search.component').then(m => m.RecipeSearchComponent),
    title: 'Recipe Search'
  },
  {
    path: 'new', // e.g., /recipes/new
    component: RecipeEditComponent,
    title: 'Create Recipe'
  },
  {
    path: ':id', // e.g., /recipes/123
    component: RecipeEditComponent,
    title: 'View Recipe'
  },
  {
    path: ':id/edit', // e.g., /recipes/123/edit
    component: RecipeEditComponent,
    title: 'Edit Recipe'
  },
  {
    path: 'ingredients/new', // e.g., /recipes/ingredients/new
    component: IngredientEditComponent,
    title: 'Create New Ingredient'
  },
  {
    path: 'ingredients/:id/edit', // e.g., /recipes/ingredients/123/edit
    component: IngredientEditComponent,
    title: 'Edit Ingredient'
  }
];