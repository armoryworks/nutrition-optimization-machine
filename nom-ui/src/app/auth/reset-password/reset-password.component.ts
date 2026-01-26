import { Component, OnInit, OnDestroy, ViewEncapsulation, inject } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, loading, AmwValidationTooltipDirective, AmwValidators, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { ResetPassword } from '../models/reset-password';
import { NotificationService } from '../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../shared/constants/error-messages';

@Component({
  selector: 'nom-reset-password',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './reset-password.component.html',
  styleUrls: ['./reset-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ResetPasswordComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private validationService = inject(AmwValidationService);

  resetPasswordForm: FormGroup;
  email = '';
  resetCode = '';
  validationContext!: ValidationContext;

  constructor() {
    // Initialize form with default values
    this.resetPasswordForm = this.nonNullableFb.group(
      {
        email: ['', [Validators.required, Validators.email]],
        resetCode: ['', Validators.required],
        newPassword: ['', [Validators.required, Validators.minLength(8)]],
        confirmNewPassword: ['', Validators.required],
      },
      { validators: AmwValidators.passwordsMatch('newPassword', 'confirmNewPassword') }
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

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Email validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-required',
      message: 'Email is required',
      severity: 'error',
      field: 'email',
      control: this.resetPasswordForm.get('email') ?? undefined,
      validator: () => !this.resetPasswordForm.get('email')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'email',
      control: this.resetPasswordForm.get('email') ?? undefined,
      validator: () => !this.resetPasswordForm.get('email')?.hasError('email')
    });

    // Reset code validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'resetCode-required',
      message: 'Reset code is required',
      severity: 'error',
      field: 'resetCode',
      control: this.resetPasswordForm.get('resetCode') ?? undefined,
      validator: () => !this.resetPasswordForm.get('resetCode')?.hasError('required')
    });

    // New password validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'newPassword-required',
      message: 'New password is required',
      severity: 'error',
      field: 'newPassword',
      control: this.resetPasswordForm.get('newPassword') ?? undefined,
      validator: () => !this.resetPasswordForm.get('newPassword')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'newPassword-minlength',
      message: 'Password must be at least 8 characters',
      severity: 'error',
      field: 'newPassword',
      control: this.resetPasswordForm.get('newPassword') ?? undefined,
      validator: () => !this.resetPasswordForm.get('newPassword')?.hasError('minlength')
    });

    // Confirm new password validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'confirmNewPassword-required',
      message: 'Confirm new password is required',
      severity: 'error',
      field: 'confirmNewPassword',
      control: this.resetPasswordForm.get('confirmNewPassword') ?? undefined,
      validator: () => !this.resetPasswordForm.get('confirmNewPassword')?.hasError('required')
    });

    // Password match validation (form-level)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'passwords-mismatch',
      message: 'Passwords do not match',
      severity: 'error',
      field: 'confirmNewPassword',
      validator: () => !this.resetPasswordForm.hasError('mismatch')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  /**
   * Handles the password reset form submission.
   */
  onSubmit(): void {
    this.resetPasswordForm.markAllAsTouched();
    this.resetPasswordForm.updateValueAndValidity();

    if (this.resetPasswordForm.invalid) {
      this.notificationService.warning(
        'Please fill all required fields and correct errors.'
      );
      return;
    }

    const formData = this.resetPasswordForm.getRawValue();
    const resetData: ResetPassword = {
      email: formData.email,
      resetCode: formData.resetCode,
      newPassword: formData.newPassword,
    };

    this.authService.resetPassword(resetData)
      .pipe(loading('Resetting password...'))
      .subscribe({
        next: () => {
          this.notificationService.success('Your password has been reset successfully!');
          this.resetPasswordForm.reset();
          this.resetPasswordForm.setErrors(null);
          this.router.navigate(['/login']);
        },
        error: (error) => {
          console.error('Password reset error:', error);
          this.notificationService.error(
            error.message || ERROR_MESSAGES.AUTH.PASSWORD_RESET_FAILED
          );
        }
      });
  }
}
