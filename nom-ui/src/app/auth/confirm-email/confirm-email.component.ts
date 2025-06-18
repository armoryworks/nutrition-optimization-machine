import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router'; // Import ActivatedRoute and Router

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatIconModule } from '@angular/material/icon'; // Often used for status icons

import { AuthService } from '../auth.service';
import { ConfirmEmail } from '../models/confirm-email';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'app-confirm-email',
  standalone: true,
  imports: [
    CommonModule,
    MatCardModule,
    MatButtonModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatIconModule,
    RouterLink, // For navigation buttons
  ],
  templateUrl: './confirm-email.component.html',
  styleUrls: ['./confirm-email.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class ConfirmEmailComponent implements OnInit {
  isLoading = true;
  confirmationStatus: 'pending' | 'success' | 'failure' = 'pending';
  confirmationMessage: string = 'Confirming your email address...';

  constructor(
    private route: ActivatedRoute, // To access query parameters
    private authService: AuthService,
    private notificationService: NotificationService, // Use NotificationService
    private router: Router // To redirect after confirmation
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe((params) => {
      const userId = params['userId'];
      const code = params['code'];
      const changedEmail = params['changedEmail'];

      if (!userId || !code) {
        this.confirmationStatus = 'failure';
        this.confirmationMessage =
          'Invalid email confirmation link. Missing user ID or code.';
        this.isLoading = false;
        this.notificationService.error(this.confirmationMessage);
        return;
      }

      const confirmationData: ConfirmEmail = {
        userId: userId,
        code: code,
        changedEmail: changedEmail || undefined, // Only include if present
      };

      this.authService.confirmEmail(confirmationData).subscribe({
        next: (response) => {
          this.isLoading = false;
          // Assuming AuthService.confirmEmail returns an object with success/message (Observable<any>)
          if (response && response.success) {
            this.confirmationStatus = 'success';
            this.confirmationMessage =
              response.message || 'Your email has been successfully confirmed!';
            this.notificationService.success(this.confirmationMessage);
            // Optionally redirect after a delay
            setTimeout(() => {
              this.router.navigate(['/login']);
            }, 3000);
          } else {
            this.confirmationStatus = 'failure';
            this.confirmationMessage =
              response?.message ||
              'Email confirmation failed. The link might be invalid or expired.';
            this.notificationService.error(this.confirmationMessage);
          }
        },
        error: (error) => {
          this.isLoading = false;
          this.confirmationStatus = 'failure';
          console.error('Email confirmation error:', error);
          // The error.message is already processed by AuthService.handleError
          this.confirmationMessage =
            error.message ||
            'An unexpected error occurred during email confirmation.';
          this.notificationService.error(this.confirmationMessage);
        },
      });
    });
  }
}
