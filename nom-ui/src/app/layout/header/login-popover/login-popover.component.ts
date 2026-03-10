import { Component, inject, output, signal, ChangeDetectionStrategy } from '@angular/core';
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

  private authService = inject(AuthService);
  private personService = inject(PersonService);
  private router = inject(Router);
  private fb = inject(FormBuilder);

  loginForm = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
  });

  loading = signal(false);
  errorMessage = signal('');
  showPassword = signal(false);

  onSubmit(): void {
    if (this.loginForm.invalid) return;

    this.loading.set(true);
    this.errorMessage.set('');

    const { email, password } = this.loginForm.getRawValue();
    this.authService.login(email!, password!).subscribe({
      next: () => {
        this.loading.set(false);
        this.closed.emit();
        this.checkOnboardingState();
      },
      error: (err) => {
        this.loading.set(false);
        this.errorMessage.set(
          err.status === 401
            ? 'Invalid email or password.'
            : 'Unable to sign in. Please try again.'
        );
      },
    });
  }

  private checkOnboardingState(): void {
    const personId = this.authService.personId();
    if (!personId) return;

    this.personService.getOnboardingState(personId).subscribe({
      next: (state) => {
        if (!state.isComplete) {
          this.router.navigate(['/onboarding']);
        }
      },
    });
  }
}
