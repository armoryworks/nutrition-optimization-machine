import { Component, OnInit, ViewEncapsulation, inject, signal } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';

import { RouterLink } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwProgressBarComponent } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { ForgotPassword } from '../models/forgot-password';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-forgot-password',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwProgressBarComponent
  ],
  templateUrl: './forgot-password.component.html',
  styleUrls: ['./forgot-password.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ForgotPasswordComponent implements OnInit {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);

  forgotPasswordForm!: FormGroup;
  isLoading = signal(false);





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

    this.isLoading.set(true);
    const data: ForgotPassword = this.forgotPasswordForm.getRawValue();

    this.authService.forgotPassword(data).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.notificationService.success(
          'Password reset link sent. Please check your inbox!'
        );
      },
      error: (error) => {
        this.isLoading.set(false);
        console.error('Forgot password error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message || 'An unexpected error occurred. Please try again.'
        );
      },
    });
  }
}
