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
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../auth.service';
import { SendConfirmationEmail } from '../models/send-confirmation-email';

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
    private snackBar: MatSnackBar
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
    if (this.sendConfirmationEmailForm.invalid) {
      this.snackBar.open('Please enter a valid email address.', 'Close', {
        duration: 3000,
      });
      return;
    }

    this.isLoading = true;
    const data: SendConfirmationEmail =
      this.sendConfirmationEmailForm.getRawValue();

    this.authService.sendConfirmationEmail(data).subscribe({
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
        console.error('Send confirmation email error:', error);
        this.snackBar.open(
          'An unexpected error occurred. Please try again.',
          'Close',
          { duration: 5000 }
        );
      },
    });
  }
}
