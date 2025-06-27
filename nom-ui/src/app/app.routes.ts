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
import { RecipeImportComponent } from './admin/components/recipe-import/recipe-import.component';

export const routes: Routes = [
  { path: 'home', component: HomeComponent },
  { path: 'register', component: RegistrationComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'send-confirmation', component: SendConfirmationEmailComponent },
  { path: 'update-info', component: UpdateInfoComponent },
  { path: 'update-two-factor', component: UpdateTwoFactorComponent },
  {
    path: 'onboarding',
    redirectTo: 'onboarding/invitationCode',
    pathMatch: 'full',
  }, // Redirect to first step
  { path: 'onboarding/:stepId', component: OnboardingWorkflowComponent }, // New route with stepId parameter
  { path: 'admin/recipe-import', component: RecipeImportComponent },
  { path: '', redirectTo: '/home', pathMatch: 'full' }, // Default route
  { path: '**', redirectTo: '/home' }, // Wildcard route for any other invalid path
];
