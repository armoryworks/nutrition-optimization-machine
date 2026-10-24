import { Component, inject, signal, computed, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';
import { identityErrorCodes, matchesPassword } from './identity-errors';

@Component({
  selector: 'nom-reset-password',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ResetPassword {
  private authService = inject(AuthService);
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private destroyRef = inject(DestroyRef);

  private queryParams = toSignal(this.route.queryParams);
  private email = computed(() => this.queryParams()?.['email'] ?? '');
  private resetCode = computed(() => this.queryParams()?.['code'] ?? '');

  resetForm = this.fb.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmNewPassword: ['', [Validators.required, matchesPassword('newPassword')]],
  });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);
  showConfirmPassword = signal(false);
  linkExpired = signal(false);
  succeeded = signal(false);
  invalidLink = computed(() => !this.email() || !this.resetCode());

  constructor() {
    this.resetForm.controls.newPassword.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.resetForm.controls.confirmNewPassword.updateValueAndValidity());
  }

  onSubmit(): void {
    if (this.resetForm.invalid) {
      this.resetForm.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');
    this.linkExpired.set(false);

    const { newPassword } = this.resetForm.getRawValue();
    this.authService.resetPassword(this.email(), this.resetCode(), newPassword!).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.loading.set(false);
        this.succeeded.set(true);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  private static readonly VALIDATION_ERRORS: Record<string, string> = {
    PasswordTooShort: 'Password must be at least 8 characters.',
    PasswordRequiresDigit: 'Password must contain at least one number.',
    PasswordRequiresUpper: 'Password must contain at least one uppercase letter.',
    PasswordRequiresLower: 'Password must contain at least one lowercase letter.',
    PasswordRequiresNonAlphanumeric: 'Password must contain at least one special character.',
    PasswordRequiresUniqueChars: 'Password must use more different characters.',
  };

  private static readonly LINK_EXPIRED = 'This reset link has expired or has already been used. Request a new one to continue.';

  private friendlyError(err: HttpErrorResponse): string {
    if (err.status === 400) {
      const messages = [...new Set(
        identityErrorCodes(err.error?.errors)
          .map(code => ResetPassword.VALIDATION_ERRORS[code])
          .filter((message): message is string => !!message),
      )];
      if (messages.length) {
        return messages.join(' ');
      }
      this.linkExpired.set(true);
      return ResetPassword.LINK_EXPIRED;
    }

    switch (err.status) {
      case 429:
        return 'Too many attempts. Please wait a moment and try again.';
      case 0:
        return 'Unable to reach the server. Please check your connection and try again.';
      default:
        return 'Something went wrong. Please try again.';
    }
  }
}
