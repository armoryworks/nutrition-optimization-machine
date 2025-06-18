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
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AuthService } from '../auth.service';
import { SendConfirmationEmail } from '../models/send-confirmation-email';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'app-send-confirmation-email',
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
    RouterLink, // Add RouterLink
  ],
  templateUrl: './send-confirmation-email.component.html',
  styleUrls: ['./send-confirmation-email.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class SendConfirmationEmailComponent implements OnInit {
  sendConfirmationEmailForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private notificationService: NotificationService // Use NotificationService instead of MatSnackBar
  ) {}

  ngOnInit(): void {
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

    this.isLoading = true;
    const data: SendConfirmationEmail =
      this.sendConfirmationEmailForm.getRawValue();

    this.authService.sendConfirmationEmail(data).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.notificationService.success(
          'Confirmation email sent. Please check your inbox!'
        );
        this.sendConfirmationEmailForm.reset(); // Optionally reset the form on success
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Send confirmation email error:', error);
        // The error.message is already processed by the AuthService's handleError
        this.notificationService.error(
          error.message || 'An unexpected error occurred. Please try again.'
        );
      },
    });
  }
}
