import { Component, OnInit, ViewEncapsulation } from '@angular/core';
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
import { LoginResponse } from '../models/login-response';
import { LoginUser } from '../models/login-user';
import { AuthManagerService } from '../../utilities/services/auth-manager.service';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'app-login',
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
export class LoginComponent implements OnInit {
  loginForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private authManager: AuthManagerService,
    private notificationService: NotificationService // Use NotificationService instead of MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loginForm = this.nonNullableFb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', Validators.required],
      rememberMe: [false],
    });
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

    this.authService.login(credentials).subscribe({
      next: (response: LoginResponse) => {
        this.isLoading = false;
        // Update AuthManagerService with login response details
        this.authManager.refreshToken = response.refreshToken;
        this.authManager.token = response.accessToken;
        this.authManager.tokenExpiration =
          response.expiresIn + new Date().getTime();

        // Display success notification
        this.notificationService.success('Logged in successfully!');
        // Optionally, navigate to a dashboard or home page after successful login
        // this.router.navigate(['/dashboard']);
      },
      error: (error: any) => {
        this.isLoading = false;
        console.error('Login error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message ||
            'An unexpected error occurred during login. Please try again.'
        );
      },
    });
  }
}
