import { Routes } from "@angular/router";
import { LoginComponent } from "./auth/components/login/login.component";
import { RegistrationComponent } from "./auth/components/registration/registration.component";
import { ForgotPasswordComponent } from "./auth/components/forgot-password/forgot-password.component";
import { ResetPasswordComponent } from "./auth/components/reset-password/reset-password.component";
import { ConfirmEmailComponent } from "./auth/components/confirm-email/confirm-email.component";
import { SendConfirmationEmailComponent } from "./auth/components/send-confirmation-email/send-confirmation-email.component";
import { UpdateInfoComponent } from "./auth/components/update-info/update-info.component";
import { UpdateTwoFactorComponent } from "./auth/components/update-two-factor/update-two-factor.component";

export const routes: Routes = [
  { path: "login", component: LoginComponent },
  { path: "register", component: RegistrationComponent },
  { path: "forgot-password", component: ForgotPasswordComponent },
  { path: "reset-password", component: ResetPasswordComponent },
  { path: "confirm-email", component: ConfirmEmailComponent },
  { path: "send-confirmation", component: SendConfirmationEmailComponent },
  { path: "update-info", component: UpdateInfoComponent },
  { path: "update-two-factor", component: UpdateTwoFactorComponent },
  { path: "", redirectTo: "/login", pathMatch: "full" }, // Default route
  { path: "**", redirectTo: "/login" }, // Wildcard route for any other invalid path
];
