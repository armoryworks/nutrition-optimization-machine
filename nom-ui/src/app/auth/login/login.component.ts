import { Component, ViewEncapsulation, inject, OnInit, OnDestroy, output } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';

import { RouterLink } from '@angular/router';

import { AmwInputComponent, AmwCheckboxComponent, AmwButtonComponent, AmwIconComponent, loading, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { LoginUser } from '../models/login-user';
import { AuthManagerService } from '../../utilities/services/auth-manager.service';
import { NotificationService } from '../../utilities/services/notification.service';
import { ERROR_MESSAGES } from '../../shared/constants/error-messages';

@Component({
  selector: 'nom-login',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AmwInputComponent,
    AmwCheckboxComponent,
    AmwButtonComponent,
    AmwIconComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class LoginComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authManager = inject(AuthManagerService);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  closeRequested = output<void>();

  loginForm: FormGroup = this.nonNullableFb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', Validators.required],
    rememberMe: [false],
  });

  validationContext!: ValidationContext;

  ngOnInit(): void {
    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Email validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-required',
      message: 'Email is required',
      severity: 'error',
      field: 'email',
      control: this.loginForm.get('email') ?? undefined,
      validator: () => !this.loginForm.get('email')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'email',
      control: this.loginForm.get('email') ?? undefined,
      validator: () => !this.loginForm.get('email')?.hasError('email')
    });

    // Password validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'password-required',
      message: 'Password is required',
      severity: 'error',
      field: 'password',
      control: this.loginForm.get('password') ?? undefined,
      validator: () => !this.loginForm.get('password')?.hasError('required')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }



  /**
   * Handles the login form submission.
   */
  onSubmit(): void {
    this.loginForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback

    if (this.loginForm.invalid) {
      // Use NotificationService for client-side validation feedback
      this.notificationService.warning(
        'Please enter a valid email and password.'
      );
      return;
    }

    const credentials: LoginUser = this.loginForm.getRawValue();
    this.authManager.rememberMe = !!credentials.rememberMe; // Ensure boolean conversion

    // Use AuthManagerService.login() with global loading overlay
    this.authManager.login(credentials)
      .pipe(loading('Signing in...'))
      .subscribe({
        next: () => {
          // Success notification is handled by AuthManagerService
        },
        error: (error: unknown) => {
          console.error('Login error:', error);
          const errorMessage = error && typeof error === 'object' && 'message' in error
            ? String(error.message)
            : ERROR_MESSAGES.AUTH.LOGIN_FAILED;
          this.notificationService.error(errorMessage);
        },
      });
  }
}
