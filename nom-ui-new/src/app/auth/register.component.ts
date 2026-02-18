import { Component, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { switchMap } from 'rxjs';
import { AuthService } from '../core/services/auth.service';

@Component({
  selector: 'nom-register',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './register.component.html',
  styleUrl: './register.component.scss',
})
export class Register {
  private authService = inject(AuthService);
  private fb = inject(FormBuilder);
  private router = inject(Router);

  registerForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    fullName: [''],
    password: ['', [Validators.required, Validators.minLength(8)]],
    confirmPassword: ['', Validators.required],
  }, { validators: this.passwordsMatch });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);
  showConfirmPassword = signal(false);

  private passwordsMatch(group: AbstractControl): ValidationErrors | null {
    const password = group.get('password')?.value;
    const confirm = group.get('confirmPassword')?.value;
    return password === confirm ? null : { passwordMismatch: true };
  }

  onSubmit(): void {
    if (this.registerForm.invalid) return;

    this.loading.set(true);
    this.errorMessage.set('');

    const { email, password, fullName } = this.registerForm.getRawValue();
    this.authService.register(email!, password!, fullName || undefined).pipe(
      switchMap(() => this.authService.loginAfterRegister(email!, password!))
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.router.navigate(['/onboarding']);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  private static readonly VALIDATION_ERRORS: Record<string, string> = {
    DuplicateUserName: 'An account with this email already exists. Try signing in instead.',
    DuplicateEmail: 'An account with this email already exists. Try signing in instead.',
    PasswordTooShort: 'Password must be at least 8 characters.',
    PasswordRequiresDigit: 'Password must contain at least one number.',
    PasswordRequiresUpper: 'Password must contain at least one uppercase letter.',
    PasswordRequiresLower: 'Password must contain at least one lowercase letter.',
    PasswordRequiresNonAlphanumeric: 'Password must contain at least one special character.',
    InvalidEmail: 'Please enter a valid email address.',
  };

  private friendlyError(err: any): string {
    if (err.status === 400 && err.error?.errors) {
      const errorKeys = Object.keys(err.error.errors);
      for (const key of errorKeys) {
        if (Register.VALIDATION_ERRORS[key]) {
          return Register.VALIDATION_ERRORS[key];
        }
      }
      return 'Please check your details and try again.';
    }

    switch (err.status) {
      case 409:
        return 'An account with this email already exists. Try signing in instead.';
      case 422:
        return 'Please check your details and try again.';
      case 429:
        return 'Too many attempts. Please wait a moment and try again.';
      case 0:
        return 'Unable to reach the server. Please check your connection and try again.';
      default:
        return 'Something went wrong creating your account. Please try again.';
    }
  }
}
