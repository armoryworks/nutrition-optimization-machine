import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router'; // Import Router for navigation

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AuthService } from '../auth.service';
import { ResetPassword } from '../models/reset-password';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'app-reset-password',
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
  ],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ResetPasswordComponent implements OnInit {
  resetPasswordForm!: FormGroup;
  isLoading = false;
  email: string = '';
  resetCode: string = '';

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private notificationService: NotificationService, // Use NotificationService
    private route: ActivatedRoute,
    private router: Router // Inject Router for navigation
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.email = params['email'] || '';
      this.resetCode = params['code'] || '';

      this.resetPasswordForm = this.nonNullableFb.group(
        {
          email: [
            { value: this.email, disabled: this.email !== '' },
            [Validators.required, Validators.email],
          ],
          resetCode: [
            { value: this.resetCode, disabled: this.resetCode !== '' },
            Validators.required,
          ],
          newPassword: ['', [Validators.required, Validators.minLength(8)]],
          confirmNewPassword: ['', Validators.required],
        },
        // Apply the custom validator to the FormGroup
        { validators: this.passwordMatchValidator }
      );
    });
  }

  /**
   * Custom validator for password matching.
   * Applied to the FormGroup.
   * @param control The FormGroup being validated.
   * @returns ValidationErrors if passwords don't match, otherwise null.
   */
  passwordMatchValidator = (
    control: AbstractControl
  ): { [key: string]: boolean } | null => {
    const newPassword = control.get('newPassword')?.value;
    const confirmNewPassword = control.get('confirmNewPassword')?.value;

    // Return null if fields are empty or not yet touched/dirty, to avoid premature errors
    if (!newPassword || !confirmNewPassword) {
      return null;
    }

    return newPassword === confirmNewPassword ? null : { mismatch: true };
  };

  /**
   * Handles the password reset form submission.
   */
  onSubmit(): void {
    this.resetPasswordForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback
    // Ensure form validation state is updated after marking touched, especially for cross-field validators
    this.resetPasswordForm.updateValueAndValidity();

    if (this.resetPasswordForm.invalid) {
      this.notificationService.warning(
        'Please fill all required fields and correct errors.'
      );
      return;
    }

    this.isLoading = true;
    // getRawValue includes disabled fields
    const formData = this.resetPasswordForm.getRawValue();
    const resetData: ResetPassword = {
      email: formData.email,
      resetCode: formData.resetCode,
      newPassword: formData.newPassword,
    };

    this.authService.resetPassword(resetData).subscribe({
      next: (response: any) => {
        // response type is 'any' as per AuthService, assuming it has .success and .message
        this.isLoading = false;
        if (response && response.success) {
          // Check for success property from backend
          this.notificationService.success(
            response.message || 'Your password has been reset successfully!'
          );
          this.resetPasswordForm.reset(); // Clear the form
          this.resetPasswordForm.setErrors(null); // Clear form-level errors after reset
          // Optionally redirect to login page after successful password reset
          this.router.navigate(['/login']);
        } else {
          // Handle cases where backend indicates failure but doesn't throw an HTTP error
          this.notificationService.error(
            response?.message || 'Password reset failed. Please try again.'
          );
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Password reset error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message ||
            'An unexpected error occurred during password reset. Please try again.'
        );
      },
    });
  }
}
