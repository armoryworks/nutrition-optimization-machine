import { Component, inject, signal, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'nom-confirm-email',
  imports: [RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  template: `
    <div class="nom-form--card">
      <div class="nom-form__card nom-form__card--narrow">
        @if (loading()) {
          <div class="nom-form__card-icon">
            <mat-spinner diameter="48"></mat-spinner>
          </div>
          <div class="nom-form__card-header">
            <h1 class="nom-form__card-title">Confirming your email...</h1>
          </div>
        } @else if (success()) {
          <div class="nom-form__card-icon">
            <mat-icon>check_circle</mat-icon>
          </div>
          <div class="nom-form__card-header">
            <h1 class="nom-form__card-title">Email Confirmed</h1>
            <p class="nom-form__card-subtitle">Your email has been confirmed. You can now sign in.</p>
          </div>
          <div class="nom-form__card-content">
            <div class="nom-form__card-actions">
              <a mat-flat-button routerLink="/home">Sign In</a>
            </div>
          </div>
        } @else {
          <div class="nom-form__card-icon">
            <mat-icon>error_outline</mat-icon>
          </div>
          <div class="nom-form__card-header">
            <h1 class="nom-form__card-title">Confirmation Failed</h1>
            <p class="nom-form__card-subtitle">{{ error() }}</p>
          </div>
          <div class="nom-form__card-content">
            <div class="nom-form__card-actions">
              <a mat-flat-button routerLink="/home">Go to Sign In</a>
            </div>
          </div>
        }
      </div>
    </div>
  `,
  styles: ``,
})
export class ConfirmEmail implements OnInit {
  private route = inject(ActivatedRoute);
  private authService = inject(AuthService);

  loading = signal(true);
  success = signal(false);
  error = signal('');

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const userId = params['userId'];
      const token = params['token'];

      if (!userId || !token) {
        this.error.set('Invalid confirmation link. Please check your email for the correct link.');
        this.loading.set(false);
        return;
      }

      this.authService.confirmEmail(userId, token).subscribe({
        next: () => {
          this.success.set(true);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(err.error?.message || 'Email confirmation failed. The link may have expired.');
          this.loading.set(false);
        },
      });
    });
  }
}
