import { Component, OnInit, OnDestroy, ViewEncapsulation, inject } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';

import { RouterLink } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { ForgotPassword } from '../models/forgot-password';
import { NotificationService } from '../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../shared/constants/error-messages';

@Component({
  selector: 'nom-forgot-password',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ForgotPasswordComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  forgotPasswordForm!: FormGroup;
  validationContext!: ValidationContext;


  ngOnInit(): void {
    this.forgotPasswordForm = this.nonNullableFb.group({
      email: ['', [Validators.required, Validators.email]],
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
      control: this.forgotPasswordForm.get('email') ?? undefined,
      validator: () => !this.forgotPasswordForm.get('email')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'email',
      control: this.forgotPasswordForm.get('email') ?? undefined,
      validator: () => !this.forgotPasswordForm.get('email')?.hasError('email')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  /**
   * Handles the forgot password form submission.
   */
  onSubmit(): void {
    this.forgotPasswordForm.markAllAsTouched();

    if (this.forgotPasswordForm.invalid) {
      this.notificationService.warning('Please enter a valid email address.');
      return;
    }

    const data: ForgotPassword = this.forgotPasswordForm.getRawValue();

    this.authService.forgotPassword(data)
      .pipe(loading('Sending reset link...'))
      .subscribe({
        next: () => {
          this.notificationService.success(
            'Password reset link sent. Please check your inbox!'
          );
        },
        error: (error) => {
          console.error('Forgot password error:', error);
          this.notificationService.error(
            error.message || ERROR_MESSAGES.UNKNOWN
          );
        },
      });
  }
}
