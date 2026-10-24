import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { guestGuard } from './core/guards/guest.guard';
import { unsavedFormGuard } from './core/guards/unsaved-form.guard';
import { adminGuard } from './core/guards/admin.guard';
import { NO_CONTEXT_RAIL } from './core/utils/context-rail';

export const routes: Routes = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'home', loadComponent: () => import('./home/home.component').then((m) => m.Home) },
  {
    path: 'recipe/new',
    loadComponent: () => import('./recipe/recipe-form.component').then((m) => m.RecipeForm),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'recipe/import',
    loadComponent: () => import('./recipe/recipe-import.component').then((m) => m.RecipeImport),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'recipe/:id/edit',
    loadComponent: () => import('./recipe/recipe-form.component').then((m) => m.RecipeForm),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'recipe/:id',
    loadComponent: () => import('./recipe/recipe-detail.component').then((m) => m.RecipeDetail),
    data: NO_CONTEXT_RAIL,
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
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'ingredient/:id/edit',
    loadComponent: () =>
      import('./ingredient/ingredient-form.component').then((m) => m.IngredientForm),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'search',
    loadComponent: () => import('./search/search.component').then((m) => m.Search),
  },
  {
    path: 'terms',
    loadComponent: () => import('./shared/terms/terms.component').then((m) => m.Terms),
  },
  {
    path: 'dishes',
    loadComponent: () => import('./dishes/dish-groups.component').then((m) => m.DishGroups),
  },
  {
    path: 'dishes/:slug',
    loadComponent: () => import('./dishes/dish-group.component').then((m) => m.DishGroup),
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/register.component').then((m) => m.Register),
    canActivate: [guestGuard],
    canDeactivate: [unsavedFormGuard],
  },
  {
    // Standalone sign-in page. No guestGuard — the page itself forwards
    // already-signed-in visitors to the right screen.
    path: 'login',
    loadComponent: () => import('./auth/login-page.component').then((m) => m.LoginPage),
  },
  {
    // Redeems a one-time sign-in handoff code from the marketing site's
    // embedded login popover (#code=… fragment). No guard — the component
    // itself routes signed-in/failed visitors appropriately.
    path: 'auth/handoff',
    loadComponent: () => import('./auth/handoff.component').then((m) => m.AuthHandoff),
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
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'profile',
    loadComponent: () => import('./profile/profile.component').then((m) => m.Profile),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'restrictions',
    loadComponent: () =>
      import('./restrictions/restrictions.component').then((m) => m.Restrictions),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'household',
    loadComponent: () => import('./household/household.component').then((m) => m.Household),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'household/:id/consent',
    loadComponent: () =>
      import('./household/enrollment-consent/enrollment-consent.component').then(
        (m) => m.EnrollmentConsent,
      ),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
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
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'plan',
    loadComponent: () => import('./plan/plan.component').then((m) => m.Plan),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'settings/security',
    loadComponent: () =>
      import('./settings/security-settings.component').then((m) => m.SecuritySettings),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'settings/api-tokens',
    loadComponent: () =>
      import('./settings/api-tokens-settings.component').then((m) => m.ApiTokensSettings),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'settings/privacy',
    loadComponent: () =>
      import('./settings/privacy-settings.component').then((m) => m.PrivacySettings),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'settings',
    loadComponent: () => import('./settings/settings.component').then((m) => m.Settings),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
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
    data: NO_CONTEXT_RAIL,
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
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'messages/:id',
    loadComponent: () => import('./messaging/thread.component').then((m) => m.Thread),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'messages',
    loadComponent: () => import('./messaging/inbox.component').then((m) => m.Inbox),
    canActivate: [authGuard],
    data: NO_CONTEXT_RAIL,
  },

  // Admin routes — tabbed shell; /admin/curation and /admin/webhooks keep
  // working as tab deep-links via the :tab parameter.
  {
    path: 'admin',
    loadComponent: () => import('./admin/admin.component').then((m) => m.Admin),
    canActivate: [authGuard, adminGuard],
    data: NO_CONTEXT_RAIL,
  },
  {
    path: 'admin/:tab',
    loadComponent: () => import('./admin/admin.component').then((m) => m.Admin),
    canActivate: [authGuard, adminGuard],
    data: NO_CONTEXT_RAIL,
  },

  {
    path: '**',
    loadComponent: () =>
      import('./shared/components/not-found/not-found.component').then((m) => m.NotFound),
  },
];
