import { Routes } from '@angular/router';
import { LoginComponent } from './components/auth/login/login.component';
import { RegistrationComponent } from './components/auth/registration/registration.component';
import { ForgotPasswordComponent } from './components/auth/forgot-password/forgot-password.component';
import { ResetPasswordComponent } from './components/auth/reset-password/reset-password.component';
import { ConfirmEmailComponent } from './components/auth/confirm-email/confirm-email.component';
import { SendConfirmationEmailComponent } from './components/auth/send-confirmation-email/send-confirmation-email.component';
import { UpdateInfoComponent } from './components/auth/update-info/update-info.component';
import { UpdateTwoFactorComponent } from './components/auth/update-two-factor/update-two-factor.component';
import { HomeComponent } from './components/home/home.component';

export const routes: Routes = [
  { path: 'home', component: HomeComponent },
  { path: 'register', component: RegistrationComponent },
  { path: 'forgot-password', component: ForgotPasswordComponent },
  { path: 'reset-password', component: ResetPasswordComponent },
  { path: 'confirm-email', component: ConfirmEmailComponent },
  { path: 'send-confirmation', component: SendConfirmationEmailComponent },
  { path: 'update-info', component: UpdateInfoComponent },
  { path: 'update-two-factor', component: UpdateTwoFactorComponent },
  { path: '', redirectTo: '/home', pathMatch: 'full' }, // Default route
  { path: '**', redirectTo: '/home' }, // Wildcard route for any other invalid path
];
