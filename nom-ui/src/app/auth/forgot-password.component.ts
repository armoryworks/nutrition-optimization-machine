import { Component, inject, signal, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'nom-forgot-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForgotPassword {
  private authService = inject(AuthService);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  forgotForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
  });

  loading = signal(false);
  submitted = signal(false);
  errorMessage = signal('');

  onSubmit(): void {
    if (this.forgotForm.invalid) return;

    this.loading.set(true);
    this.errorMessage.set('');

    const { email } = this.forgotForm.getRawValue();
    this.authService.forgotPassword(email!).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.loading.set(false);
        this.submitted.set(true);
      },
      error: (err) => {
        this.loading.set(false);
        // Always show success to avoid revealing which emails are registered.
        // Only show errors for network/rate-limit issues.
        if (err.status === 0) {
          this.errorMessage.set('Unable to reach the server. Please check your connection and try again.');
        } else if (err.status === 429) {
          this.errorMessage.set('Too many attempts. Please wait a moment and try again.');
        } else {
          this.submitted.set(true);
        }
      },
    });
  }
}
