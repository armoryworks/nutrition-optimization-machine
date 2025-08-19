import { Component, OnInit, ViewEncapsulation, inject } from '@angular/core';
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
  selector: 'nom-confirm-email',
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
  private route = inject(ActivatedRoute);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private router = inject(Router);

  isLoading = true;
  confirmationStatus: 'pending' | 'success' | 'failure' = 'pending';
  confirmationMessage = 'Confirming your email address...';





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

      this.authService.confirmEmail(confirmationData).subscribe(
        () => {
          this.isLoading = false;
          this.confirmationStatus = 'success';
          this.confirmationMessage = 'Your email has been successfully confirmed!';
          this.notificationService.success(this.confirmationMessage);
          // Optionally redirect after a delay
          setTimeout(() => {
            this.router.navigate(['/login']);
          }, 3000);
        },
        (error) => {
          this.isLoading = false;
          this.confirmationStatus = 'failure';
          console.error('Email confirmation error:', error);
          // The error.message is already processed by AuthService.handleError
          this.confirmationMessage =
            error.message ||
            'An unexpected error occurred during email confirmation.';
          this.notificationService.error(this.confirmationMessage);
        }
      );
    });
  }
}
