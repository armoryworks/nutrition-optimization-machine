import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';

import { ActivatedRoute, Router } from '@angular/router';

import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { ResetPassword } from '../models/reset-password';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-reset-password',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatProgressBarModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent
  ],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ResetPasswordComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  resetPasswordForm: FormGroup;
  isLoading = signal(false);
  email = '';
  resetCode = '';



  constructor() {
    // Initialize form with default values
    this.resetPasswordForm = this.nonNullableFb.group(
      {
        email: ['', [Validators.required, Validators.email]],
        resetCode: ['', Validators.required],
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmNewPassword: ['', Validators.required],
      },
      // Apply the custom validator to the FormGroup
      { validators: this.passwordMatchValidator }
    );
  }

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      this.email = params['email'] || '';
      this.resetCode = params['code'] || '';

      // Update form values and disabled state based on route parameters
      this.resetPasswordForm.patchValue({
        email: this.email,
        resetCode: this.resetCode,
      });

      // Disable fields if they have values from route parameters
      if (this.email) {
        this.resetPasswordForm.get('email')?.disable();
      }
      if (this.resetCode) {
        this.resetPasswordForm.get('resetCode')?.disable();
      }
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
  ): Record<string, boolean> | null => {
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

    this.isLoading.set(true);
    // getRawValue includes disabled fields
    const formData = this.resetPasswordForm.getRawValue();
    const resetData: ResetPassword = {
      email: formData.email,
      resetCode: formData.resetCode,
      newPassword: formData.newPassword,
    };

    this.authService.resetPassword(resetData).subscribe(
      () => {
        this.isLoading.set(false);
        this.notificationService.success('Your password has been reset successfully!');
        this.resetPasswordForm.reset(); // Clear the form
        this.resetPasswordForm.setErrors(null); // Clear form-level errors after reset
        // Optionally redirect to login page after successful password reset
        this.router.navigate(['/login']);
      },
      (error) => {
        this.isLoading.set(false);
        console.error('Password reset error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message ||
          'An unexpected error occurred during password reset. Please try again.'
        );
      }
    );
  }
}
