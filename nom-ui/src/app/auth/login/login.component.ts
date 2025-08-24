import { Component, ViewEncapsulation, inject } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router'; // Import RouterLink for button navigation

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AuthService } from '../auth.service';

import { LoginUser } from '../models/login-user';
import { AuthManagerService } from '../../utilities/services/auth-manager.service';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-login',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    RouterLink, // Add RouterLink
  ],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class LoginComponent {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private authManager = inject(AuthManagerService);
  private notificationService = inject(NotificationService);

  loginForm: FormGroup = this.nonNullableFb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    rememberMe: [false],
  });

  isLoading = false;

  constructor() {
    // Form is now initialized at declaration
  }



  /**
   * Handles the login form submission.
   */
  onSubmit(): void {
    this.loginForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback

    if (this.loginForm.invalid) {
      // Use NotificationService for client-side validation feedback
      this.notificationService.warning(
        'Please enter a valid email and password.'
      );
      return;
    }

    this.isLoading = true;
    const credentials: LoginUser = this.loginForm.getRawValue();
    this.authManager.rememberMe = !!credentials.rememberMe; // Ensure boolean conversion

    // Use AuthManagerService.login() instead of AuthService.login()
    this.authManager.login(credentials).subscribe({
      next: () => {
        this.isLoading = false;
        // Success notification is handled by AuthManagerService
        // Optionally, navigate to a dashboard or home page after successful login
        // this.router.navigate(['/dashboard']);
      },
      error: (error: unknown) => {
        this.isLoading = false;
        console.error('Login error:', error);
        // The error.message is already processed by the AuthManagerService
        const errorMessage = error && typeof error === 'object' && 'message' in error ? String(error.message) : 'An unexpected error occurred during login. Please try again.';
        this.notificationService.error(errorMessage);
      },
    });
  }
}
