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
  });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);

  onSubmit(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }

    this.loading.set(true);
    this.errorMessage.set('');

    const { email, password } = this.loginForm.getRawValue();
    this.authService
      .login(email!, password!)
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
          this.errorMessage.set(
            err.status === 401
              ? 'Invalid email or password.'
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
