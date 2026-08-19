import {
  Component,
  booleanAttribute,
  inject,
  input,
  output,
  signal,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../../core/services/auth.service';
import { PersonService } from '../../../core/services/person.service';

@Component({
  selector: 'nom-login-popover',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './login-popover.component.html',
  styleUrl: './login-popover.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoginPopover {
  closed = output<void>();
  /** Emitted after a successful login when the host handles navigation itself. */
  loggedIn = output<void>();
  /** When true, skip the built-in post-login onboarding redirect and emit loggedIn instead. */
  deferNavigation = input(false);
  /**
   * Renders a close button inside the card (top-right). The button only emits
   * `closed` — the host owns the dismissal behavior. Off by default: the
   * header popover and /login page have their own dismissal affordances.
   */
  showClose = input(false, { transform: booleanAttribute });

  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private router = inject(Router);
  private fb = inject(FormBuilder);
  private destroyRef = inject(DestroyRef);

  loginForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    twoFactorCode: [''],
  });

  /** True after Identity answered RequiresTwoFactor — the code field is shown. */
  needsTwoFactor = signal(false);
  /** Entering a recovery code instead of an authenticator code. */
  useRecoveryCode = signal(false);

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);
  /** True after a 401 NotAllowed — offers to resend the confirmation email. */
  needsConfirmation = signal(false);
  resendState = signal<'idle' | 'sending' | 'sent'>('idle');

  resendConfirmation(): void {
    const email = this.loginForm.getRawValue().email;
    if (!email || this.resendState() === 'sending') return;
    this.resendState.set('sending');
    this.authService.resendConfirmation(email)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.resendState.set('sent'),
        error: () => this.resendState.set('sent'), // endpoint is deliberately opaque; treat as sent
      });
  }

  onSubmit(): void {
    if (this.loginForm.invalid) {
      // Silent early-return looked like a dead button; say what is wrong.
      this.loginForm.markAllAsTouched();
      const emailCtrl = this.loginForm.controls.email;
      this.errorMessage.set(
        emailCtrl.hasError('email')
          ? 'Please sign in with your email address (not a username).'
          : 'Please enter your email and password.',
      );
      return;
    }
    if (this.needsTwoFactor() && !this.loginForm.getRawValue().twoFactorCode?.trim()) {
      this.errorMessage.set(this.useRecoveryCode() ? 'Enter one of your recovery codes.' : 'Enter the 6-digit code from your authenticator app.');
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');

    const { email, password, twoFactorCode } = this.loginForm.getRawValue();
    const twoFactor = this.needsTwoFactor() && twoFactorCode
      ? (this.useRecoveryCode() ? { recoveryCode: twoFactorCode } : { code: twoFactorCode })
      : undefined;
    this.authService
      .login(email!.trim(), password!, twoFactor)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.loading.set(false);
          if (this.deferNavigation()) {
            this.loggedIn.emit();
            return;
          }
          this.closed.emit();
          this.checkOnboardingState();
        },
        error: (err) => {
          this.loading.set(false);
          // Identity's login endpoint answers 401 + detail "NotAllowed" for an
          // account whose email is not yet confirmed (RequireConfirmedEmail).
          const detail: unknown = err?.error?.detail;
          if (err.status === 401 && detail === 'RequiresTwoFactor') {
            this.needsTwoFactor.set(true);
            this.errorMessage.set(this.loginForm.getRawValue().twoFactorCode
              ? 'That code was not accepted. Check your authenticator app and try again.'
              : 'Two-factor authentication is on for this account — enter your authenticator code.');
            return;
          }
          if (err.status === 401 && detail === 'NotAllowed') {
            this.needsConfirmation.set(true);
            this.errorMessage.set('Please confirm your email address before signing in.');
            return;
          }
          this.needsConfirmation.set(false);
          this.errorMessage.set(
            err.status === 401
              ? (detail === 'LockedOut'
                  ? 'This account is temporarily locked after too many attempts. Try again in 15 minutes.'
                  : 'Invalid email or password.')
              : 'Unable to sign in. Please try again.',
          );
        },
      });
  }

  private checkOnboardingState(): void {
    const personId = this.authService.personId();
    if (!personId) return;

    this.personService
      .getOnboardingState(personId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (state) => {
          if (!state.isComplete) {
            this.router.navigate(['/onboarding']);
          }
        },
      });
  }
}
