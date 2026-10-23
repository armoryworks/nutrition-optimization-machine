import { Component, ElementRef, inject, signal, computed, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../core/services/auth.service';
import { revealFirstInvalid } from '../core/utils/reveal-invalid-control';
import { LoadingService } from '../core/services/loading.service';
import { HasUnsavedForm } from '../core/guards/unsaved-form.guard';
import { authorizeReturnUrl } from './oidc-return-url';
import { identityErrorCodes, matchesPassword, PASSWORD_RULES, passwordRulesValidator } from './identity-errors';

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
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Register implements HasUnsavedForm {
  private authService = inject(AuthService);
  private loadingService = inject(LoadingService);
  private host = inject<ElementRef<HTMLElement>>(ElementRef);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  readonly returnUrl = authorizeReturnUrl(inject(ActivatedRoute).snapshot.queryParamMap.get('returnUrl'));

  registerForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    fullName: [''],
    password: ['', [Validators.required, passwordRulesValidator]],
    confirmPassword: ['', [Validators.required, matchesPassword('password')]],
  });

  readonly passwordRules = PASSWORD_RULES;
  private readonly passwordValue = toSignal(this.registerForm.controls.password.valueChanges, { initialValue: '' });
  readonly passwordRuleStates = computed(() => {
    const value = this.passwordValue() ?? '';
    return this.passwordRules.map(rule => ({ rule, met: rule.test(value) }));
  });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);
  showConfirmPassword = signal(false);
  registeredEmail = signal('');
  resendSuccess = signal(false);

  hasUnsavedChanges(): boolean {
    if (this.registeredEmail()) return false;
    const { email, fullName, password } = this.registerForm.getRawValue();
    return !!(email || fullName || password);
  }

  constructor() {
    this.registerForm.controls.password.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.registerForm.controls.confirmPassword.updateValueAndValidity());
  }

  onSubmit(): void {
    if (this.registerForm.invalid) {
      revealFirstInvalid(this.registerForm, this.host.nativeElement);
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');

    const { email, password, fullName } = this.registerForm.getRawValue();
    this.authService.register(email!, password!, fullName || undefined, this.returnUrl).pipe(
      this.loadingService.loading('Creating your account...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => {
        this.loading.set(false);
        this.registeredEmail.set(email!);
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(this.friendlyError(err));
      },
    });
  }

  onResendConfirmation(): void {
    const email = this.registeredEmail();
    if (!email) return;

    this.authService.resendConfirmation(email, this.returnUrl).pipe(
      this.loadingService.loading('Resending confirmation email...'),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: () => this.resendSuccess.set(true),
      error: () => this.errorMessage.set('Failed to resend confirmation email.'),
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

  private friendlyError(err: HttpErrorResponse): string {
    if (err.status === 400 && err.error?.errors) {
      const messages = [...new Set(
        identityErrorCodes(err.error.errors)
          .map(code => Register.VALIDATION_ERRORS[code])
          .filter((message): message is string => !!message),
      )];
      return messages.length ? messages.join(' ') : 'Please check your details and try again.';
    }
    if (err.status === 400 && typeof err.error?.message === 'string' && /already exists/i.test(err.error.message)) {
      return Register.VALIDATION_ERRORS['DuplicateEmail'];
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
