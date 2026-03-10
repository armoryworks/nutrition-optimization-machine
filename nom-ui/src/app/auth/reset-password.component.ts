import { Component, inject, signal, computed } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';

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
})
export class ResetPassword {
  private authService = inject(AuthService);
  private fb = inject(FormBuilder);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  private queryParams = toSignal(this.route.queryParams);
  private email = computed(() => this.queryParams()?.['email'] ?? '');
  private token = computed(() => this.queryParams()?.['code'] ?? '');

  resetForm = this.fb.group({
    newPassword: ['', [Validators.required, Validators.minLength(8)]],
    confirmNewPassword: ['', Validators.required],
  }, { validators: this.passwordsMatch });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);
  showConfirmPassword = signal(false);
  invalidLink = computed(() => !this.email() || !this.token());

  private passwordsMatch(group: AbstractControl): ValidationErrors | null {
    const password = group.get('newPassword')?.value;
    const confirm = group.get('confirmNewPassword')?.value;
    return password === confirm ? null : { passwordMismatch: true };
  }

  onSubmit(): void {
    if (this.resetForm.invalid) return;

    this.loading.set(true);
    this.errorMessage.set('');

    const { newPassword, confirmNewPassword } = this.resetForm.getRawValue();
    this.authService.resetPassword(this.email(), this.token(), newPassword!, confirmNewPassword!).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/home']);
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
    InvalidToken: 'This reset link has expired or is invalid. Please request a new one.',
  };

  private friendlyError(err: HttpErrorResponse): string {
    if (err.status === 400 && err.error?.errors) {
      const errorKeys = Object.keys(err.error.errors);
      for (const key of errorKeys) {
        if (ResetPassword.VALIDATION_ERRORS[key]) {
          return ResetPassword.VALIDATION_ERRORS[key];
        }
      }
    }

    switch (err.status) {
      case 400:
        return 'This reset link has expired or is invalid. Please request a new one.';
      case 429:
        return 'Too many attempts. Please wait a moment and try again.';
      case 0:
        return 'Unable to reach the server. Please check your connection and try again.';
      default:
        return 'Something went wrong. Please try again.';
    }
  }
}
