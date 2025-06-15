import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AuthService } from '../auth.service';
import { ForgotPassword } from '../models/forgot-password';
import { NotificationService } from '../../../utilities/services/notification.service'; // Adjust path if necessary

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    RouterLink, // Add RouterLink for navigation
  ],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ForgotPasswordComponent implements OnInit {
  forgotPasswordForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private notificationService: NotificationService // Use NotificationService
  ) {}

  ngOnInit(): void {
    this.forgotPasswordForm = this.nonNullableFb.group({
      email: ['', [Validators.required, Validators.email]],
    });
  }

  /**
   * Handles the forgot password form submission.
   */
  onSubmit(): void {
    this.forgotPasswordForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback

    if (this.forgotPasswordForm.invalid) {
      this.notificationService.warning('Please enter a valid email address.');
      return;
    }

    this.isLoading = true;
    const data: ForgotPassword = this.forgotPasswordForm.getRawValue();

    this.authService.forgotPassword(data).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.notificationService.success(
          'Password reset link sent. Please check your inbox!'
        );
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Forgot password error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message || 'An unexpected error occurred. Please try again.'
        );
      },
    });
  }
}
