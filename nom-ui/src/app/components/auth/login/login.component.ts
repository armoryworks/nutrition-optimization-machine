import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
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
import { AuthManagerService } from '../../../utilities/services/auth-manager.service';
import { LoginResponse } from '../models/login-response';
import { LoginUser } from '../models/login-user';

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
    private snackBar: MatSnackBar
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
    if (this.loginForm.invalid) {
      this.snackBar.open('Please enter your email and password.', 'Close', {
        duration: 3000,
      });
      return;
    }

    this.isLoading = true;
    const credentials: LoginUser = this.loginForm.getRawValue();
    this.authManager.rememberMe = !!credentials.rememberMe;
    this.authService.login(credentials).subscribe({
      next: (response: LoginResponse) => {
        this.isLoading = false;
        this.authManager.refreshToken = response.refreshToken;
        this.authManager.token = response.accessToken;
        this.authManager.tokenExpiration =
          response.expiresIn + new Date().getTime();
      },
      error: (error: any) => {
        this.isLoading = false;
        console.error('Login error:', error);
        this.snackBar.open(
          'An unexpected error occurred. Please try again.',
          'Close',
          { duration: 5000 }
        );
      },
    });
  }
}
