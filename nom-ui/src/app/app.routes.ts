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
import { AuthGuard } from './guards/auth.guard';


export const routes: Routes = [
  // Eager-loaded routes for immediate access
  { path: 'home', component: HomeComponent },
  { path: 'register', component: RegistrationComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'send-confirmation', component: SendConfirmationEmailComponent },
  { path: 'update-info', component: UpdateInfoComponent, canActivate: [AuthGuard] },
  { path: 'update-two-factor', component: UpdateTwoFactorComponent, canActivate: [AuthGuard] },
  {
    path: 'onboarding',
    redirectTo: 'onboarding/invitationCode',
    pathMatch: 'full',
  },
  { path: 'onboarding/:stepId', component: OnboardingWorkflowComponent, canActivate: [AuthGuard] },
  { path: 'privacy-settings', component: PrivacySettingsComponent, canActivate: [AuthGuard] },
  { path: 'ingredient-search', component: IngredientSearchComponent, canActivate: [AuthGuard] },

  // --- NEW LAZY-LOADED FEATURE ROUTES ---
  {
    path: 'recipes',
    loadChildren: () => import('./recipe/recipe.routes').then(m => m.RECIPE_ROUTES),
    canActivate: [AuthGuard]
  },
  {
    path: 'curation',
    loadChildren: () => import('./curation/curation.routes').then(m => m.CURATION_ROUTES),
    canActivate: [AuthGuard] // In a real app, this would use a specific role guard
  },
  {
    path: 'admin',
    loadChildren: () => import('./admin/admin.routes').then(m => m.ADMIN_ROUTES),
    canActivate: [AuthGuard] // In a real app, this would use a specific role guard
  },
  {
    path: 'messaging',
    loadChildren: () => import('./communication/communication.routes').then(m => m.COMMUNICATION_ROUTES),
    canActivate: [AuthGuard]
  },

  // --- Default and Wildcard routes MUST be last ---
  { path: '', redirectTo: '/home', pathMatch: 'full' }, // Default route
  { path: '**', redirectTo: '/home' }, // Wildcard route for any other invalid path
];