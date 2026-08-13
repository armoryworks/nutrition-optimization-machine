import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';
import { adminGuard } from './core/guards/admin.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'home', loadComponent: () => import('./home/home.component').then((m) => m.Home) },
  {
    path: 'recipe/new',
    loadComponent: () => import('./recipe/recipe-form.component').then((m) => m.RecipeForm),
    canActivate: [authGuard],
  },
  {
    path: 'recipe/import',
    loadComponent: () => import('./recipe/recipe-import.component').then((m) => m.RecipeImport),
    canActivate: [authGuard],
  },
  {
    path: 'recipe/:id/edit',
    loadComponent: () => import('./recipe/recipe-form.component').then((m) => m.RecipeForm),
    canActivate: [authGuard],
  },
  {
    path: 'recipe/:id',
    loadComponent: () => import('./recipe/recipe-detail.component').then((m) => m.RecipeDetail),
  },
  {
    path: 'recipes/mine',
    loadComponent: () => import('./recipe/my-recipes.component').then((m) => m.MyRecipes),
    canActivate: [authGuard],
  },
  {
    path: 'ingredients/mine',
    loadComponent: () =>
      import('./ingredient/my-ingredients.component').then((m) => m.MyIngredients),
    canActivate: [authGuard],
  },
  {
    path: 'ingredient/new',
    loadComponent: () =>
      import('./ingredient/ingredient-form.component').then((m) => m.IngredientForm),
    canActivate: [authGuard],
  },
  {
    path: 'ingredient/:id/edit',
    loadComponent: () =>
      import('./ingredient/ingredient-form.component').then((m) => m.IngredientForm),
    canActivate: [authGuard],
  },
  {
    path: 'search',
    loadComponent: () => import('./search/search.component').then((m) => m.Search),
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/register.component').then((m) => m.Register),
    canActivate: [guestGuard],
  },
  {
    // Standalone sign-in page; also embedded by the marketing site's login
    // popover (?embedded=1). No guestGuard — the page itself forwards
    // already-signed-in visitors to the right screen (incl. iframe breakout).
    path: 'login',
    loadComponent: () => import('./auth/login-page.component').then((m) => m.LoginPage),
  },
  {
    path: 'forgot-password',
    loadComponent: () => import('./auth/forgot-password.component').then((m) => m.ForgotPassword),
  },
  {
    path: 'confirm-email',
    loadComponent: () => import('./auth/confirm-email.component').then((m) => m.ConfirmEmail),
  },
  {
    path: 'reset-password',
    loadComponent: () => import('./auth/reset-password.component').then((m) => m.ResetPassword),
  },

  // Protected routes
  {
    path: 'onboarding',
    loadComponent: () => import('./onboarding/onboarding.component').then((m) => m.Onboarding),
    canActivate: [authGuard],
  },
  {
    path: 'profile',
    loadComponent: () => import('./profile/profile.component').then((m) => m.Profile),
    canActivate: [authGuard],
  },
  {
    path: 'restrictions',
    loadComponent: () =>
      import('./restrictions/restrictions.component').then((m) => m.Restrictions),
    canActivate: [authGuard],
  },
  {
    path: 'household',
    loadComponent: () => import('./household/household.component').then((m) => m.Household),
    canActivate: [authGuard],
  },
  {
    path: 'plan/curated',
    loadComponent: () => import('./plan/curated-plans.component').then((m) => m.CuratedPlans),
    canActivate: [authGuard],
  },
  {
    path: 'plan/rules',
    loadComponent: () => import('./plan/plan-rules.component').then((m) => m.PlanRules),
    canActivate: [authGuard],
  },
  {
    path: 'plan',
    loadComponent: () => import('./plan/plan.component').then((m) => m.Plan),
    canActivate: [authGuard],
  },
  {
    path: 'settings/security',
    loadComponent: () =>
      import('./settings/security-settings.component').then((m) => m.SecuritySettings),
    canActivate: [authGuard],
  },
  {
    path: 'settings/privacy',
    loadComponent: () =>
      import('./settings/privacy-settings.component').then((m) => m.PrivacySettings),
    canActivate: [authGuard],
  },
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings.component').then((m) => m.Settings),
    canActivate: [authGuard],
  },
  {
    path: 'pantry',
    loadChildren: () => import('./pantry/pantry.routes').then((m) => m.PANTRY_ROUTES),
    canActivate: [authGuard],
  },
  {
    path: 'shopping',
    loadChildren: () => import('./shopping/shopping.routes').then((m) => m.SHOPPING_ROUTES),
    canActivate: [authGuard],
  },
  {
    path: 'cookbooks',
    loadComponent: () => import('./cookbook/cookbook-list.component').then((m) => m.CookbookList),
    canActivate: [authGuard],
  },
  {
    path: 'cookbook/:id',
    loadComponent: () =>
      import('./cookbook/cookbook-detail.component').then((m) => m.CookbookDetail),
    canActivate: [authGuard],
  },
  {
    path: 'messages/new',
    loadComponent: () => import('./messaging/compose.component').then((m) => m.Compose),
    canActivate: [authGuard],
  },
  {
    path: 'messages/:id',
    loadComponent: () => import('./messaging/thread.component').then((m) => m.Thread),
    canActivate: [authGuard],
  },
  {
    path: 'messages',
    loadComponent: () => import('./messaging/inbox.component').then((m) => m.Inbox),
    canActivate: [authGuard],
  },

  // Admin routes — tabbed shell; /admin/curation and /admin/webhooks keep
  // working as tab deep-links via the :tab parameter.
  {
    path: 'admin',
    loadComponent: () => import('./admin/admin.component').then((m) => m.Admin),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/:tab',
    loadComponent: () => import('./admin/admin.component').then((m) => m.Admin),
    canActivate: [authGuard, adminGuard],
  },

  {
    path: '**',
    loadComponent: () =>
      import('./shared/components/not-found/not-found.component').then((m) => m.NotFound),
  },
];
