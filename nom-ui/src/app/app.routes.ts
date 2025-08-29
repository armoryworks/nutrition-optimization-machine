// File: nom-ui/src/app/app.routes.ts

import { Routes } from '@angular/router';
import { RegistrationComponent } from './auth/registration/registration.component';
import { ForgotPasswordComponent } from './auth/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './auth/reset-password/reset-password.component';
import { ConfirmEmailComponent } from './auth/confirm-email/confirm-email.component';
import { SendConfirmationEmailComponent } from './auth/send-confirmation-email/send-confirmation-email.component';
import { UpdateInfoComponent } from './auth/update-info/update-info.component';
import { UpdateTwoFactorComponent } from './auth/update-two-factor/update-two-factor.component';
import { HomeComponent } from './home/home.component';
import { OnboardingWorkflowComponent } from './onboarding/components/onboarding-workflow/onboarding-workflow.component';
import { PrivacySettingsComponent } from './user/components/privacy-settings/privacy-settings.component';
import { IngredientSearchComponent } from './recipe/components/ingredient-search/ingredient-search.component';
import { PersonProfileEditComponent } from './person/components/person-profile-edit/person-profile-edit.component';
import { AuthGuard } from './guards/auth.guard';

export const routes: Routes = [
  // Public routes - accessible to everyone
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'home', component: HomeComponent },
  { path: 'about', component: HomeComponent }, // Show home content for now since about page isn't implemented

  // Auth routes - accessible to everyone (using existing structure)
  { path: 'register', component: RegistrationComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'send-confirmation', component: SendConfirmationEmailComponent },

  // Common redirects
  { path: 'curations', redirectTo: 'curation', pathMatch: 'full' },
  { path: 'profile', redirectTo: 'user/profile', pathMatch: 'full' },
  { path: 'settings', redirectTo: 'user/settings', pathMatch: 'full' },
  { path: 'dashboard', redirectTo: 'home', pathMatch: 'full' },
  { path: 'plans', redirectTo: 'curated-plans', pathMatch: 'full' },
  { path: 'plan', redirectTo: 'curated-plans', pathMatch: 'full' },

  // Protected routes - require authentication (using existing structure)
  {
    path: 'recipes',
    loadChildren: () => import('./recipe/recipe.routes').then(m => m.RECIPE_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'ingredient-search',
    loadComponent: () => import('./recipe/components/ingredient-search/ingredient-search.component').then(m => m.IngredientSearchComponent),
    canActivate: [AuthGuard]
  },
  {
    path: 'curation',
    loadChildren: () => import('./curation/curation.routes').then(m => m.CURATION_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'admin',
    loadChildren: () => import('./admin/admin.routes').then(m => m.ADMIN_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'messaging',
    loadChildren: () => import('./communication/communication.routes').then(m => m.COMMUNICATION_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'communication',
    loadChildren: () => import('./communication/communication.routes').then(m => m.COMMUNICATION_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'user',
    loadChildren: () => import('./user/user.routes').then(m => m.USER_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'household',
    loadChildren: () => import('./household/household.routes').then(m => m.HOUSEHOLD_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'shopping',
    loadChildren: () => import('./shopping/shopping.routes').then(m => m.SHOPPING_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'meal-plan',
    loadChildren: () => import('./meal-plan/meal-plan.routes').then(m => m.MEAL_PLAN_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'onboarding',
    redirectTo: 'onboarding/invitationCode',
    pathMatch: 'full'
  },
  {
    path: 'onboarding/:stepId',
    component: OnboardingWorkflowComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'privacy-settings',
    component: PrivacySettingsComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'update-info',
    component: UpdateInfoComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'update-two-factor',
    component: UpdateTwoFactorComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'edit-profile',
    component: PersonProfileEditComponent,
    canActivate: [AuthGuard]
  },
  {
    path: 'curated-plans',
    loadComponent: () => import('./plan/components/curated-plans/curated-plans.component').then(m => m.CuratedPlansComponent),
    canActivate: [AuthGuard]
  }
];