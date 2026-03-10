import { Component, inject, signal, effect, ChangeDetectionStrategy } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'nom-confirm-email',
  imports: [RouterLink, MatIconModule, MatButtonModule, MatProgressSpinnerModule],
  templateUrl: './confirm-email.component.html',
  styleUrl: './confirm-email.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmEmail {
  private route = inject(ActivatedRoute);
  private authService = inject(AuthService);

  private queryParams = toSignal(this.route.queryParams);

  loading = signal(true);
  success = signal(false);
  error = signal('');

  constructor() {
    effect(() => {
      const params = this.queryParams();
      if (!params) return;

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
        error: (err: { error?: { message?: string } }) => {
          this.error.set(err.error?.message || 'Email confirmation failed. The link may have expired.');
          this.loading.set(false);
        },
      });
    });
  }
}
