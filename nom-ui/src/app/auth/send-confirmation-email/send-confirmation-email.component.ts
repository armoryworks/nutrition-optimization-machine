import { Component, ViewEncapsulation, inject, signal } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';

import { RouterLink } from '@angular/router';

import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { SendConfirmationEmail } from '../models/send-confirmation-email';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-send-confirmation-email',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatProgressBarModule,
    RouterLink,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent
  ],
  templateUrl: './send-confirmation-email.component.html',
  styleUrls: ['./send-confirmation-email.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SendConfirmationEmailComponent {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);

  sendConfirmationEmailForm: FormGroup;
  isLoading = signal(false);



  constructor() {
    this.sendConfirmationEmailForm = this.nonNullableFb.group({
      email: ['', [Validators.required, Validators.email]],
    });
  }



  /**
   * Handles the form submission to send a confirmation email.
   */
  onSubmit(): void {
    this.sendConfirmationEmailForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback

    if (this.sendConfirmationEmailForm.invalid) {
      this.notificationService.warning('Please enter a valid email address.');
      return;
    }

    this.isLoading.set(true);
    const data: SendConfirmationEmail =
      this.sendConfirmationEmailForm.getRawValue();

    this.authService.sendConfirmationEmail(data).subscribe({
      next: () => {
        this.isLoading.set(false);
        this.notificationService.success(
          'Confirmation email sent. Please check your inbox!'
        );
        this.sendConfirmationEmailForm.reset(); // Optionally reset the form on success
      },
      error: (error) => {
        this.isLoading.set(false);
        console.error('Send confirmation email error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message || 'An unexpected error occurred. Please try again.'
        );
      },
    });
  }
}
