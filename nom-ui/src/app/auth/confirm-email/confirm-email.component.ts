import { Component, OnInit, ViewEncapsulation, inject } from '@angular/core';

import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { AmwButtonComponent, AmwCardComponent, AmwIconComponent, AmwProgressSpinnerComponent, loading } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { ConfirmEmail } from '../models/confirm-email';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-confirm-email',
  standalone: true,
  imports: [
    RouterLink,
    AmwButtonComponent,
    AmwCardComponent,
    AmwIconComponent,
    AmwProgressSpinnerComponent,
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
        this.notificationService.error(this.confirmationMessage);
        return;
      }

      const confirmationData: ConfirmEmail = {
        userId: userId,
        code: code,
        changedEmail: changedEmail || undefined,
      };

      this.authService.confirmEmail(confirmationData)
        .pipe(loading('Confirming your email...'))
        .subscribe({
          next: () => {
            this.confirmationStatus = 'success';
            this.confirmationMessage = 'Your email has been successfully confirmed!';
            this.notificationService.success(this.confirmationMessage);
            setTimeout(() => {
              this.router.navigate(['/login']);
            }, 3000);
          },
          error: (error) => {
            this.confirmationStatus = 'failure';
            console.error('Email confirmation error:', error);
            this.confirmationMessage =
              error.message ||
              'An unexpected error occurred during email confirmation.';
            this.notificationService.error(this.confirmationMessage);
          }
        });
    });
  }
}
