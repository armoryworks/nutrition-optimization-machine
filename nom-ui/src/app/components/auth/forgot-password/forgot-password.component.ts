import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
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
    private snackBar: MatSnackBar
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
    if (this.forgotPasswordForm.invalid) {
      this.snackBar.open('Please enter a valid email address.', 'Close', {
        duration: 3000,
      });
      return;
    }

    this.isLoading = true;
    const data: ForgotPassword = this.forgotPasswordForm.getRawValue();

    this.authService.forgotPassword(data).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.snackBar.open(response.message, 'Dismiss', { duration: 7000 });
        } else {
          this.snackBar.open(response.message, 'Close', { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Forgot password error:', error);
        this.snackBar.open(
          'An unexpected error occurred. Please try again.',
          'Close',
          { duration: 5000 }
        );
      },
    });
  }
}
