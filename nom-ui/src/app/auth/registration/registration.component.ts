import { Component, ViewEncapsulation, inject, OnInit, OnDestroy } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
} from '@angular/forms';
import { Router } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, loading, AmwValidationTooltipDirective, AmwValidators, AmwValidationService, ValidationContext } from 'angular-material-wrap';
import { switchMap } from 'rxjs/operators';

import { AuthService } from '../auth.service';
import { RegisterUser } from '../models/register-user';
import { NotificationService } from '../../utilities/services/notification.service';
import { AuthManagerService } from '../../utilities/services/auth-manager.service';
import { ERROR_MESSAGES } from '../../shared/constants/error-messages';
import { passwordValidators, PASSWORD_REQUIREMENTS } from '../../shared/validators/password-validators';
import { PasswordRequirementsComponent } from '../../shared/components/password-requirements/password-requirements.component';

@Component({
  selector: 'nom-registration',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwValidationTooltipDirective,
    PasswordRequirementsComponent
  ],
  templateUrl: './registration.component.html',
  styleUrls: ['./registration.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class RegistrationComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private router = inject(Router);
  private authManagerService = inject(AuthManagerService);
  private validationService = inject(AmwValidationService);

  registrationForm!: FormGroup;
  validationContext!: ValidationContext;

  constructor() {
    this.registrationForm = this.nonNullableFb.group(
      {
        email: ['', [Validators.required, Validators.email]],
        fullName: ['', Validators.maxLength(100)],
        password: ['', [Validators.required, ...passwordValidators()]],
        confirmPassword: ['', Validators.required],
      },
      { validators: AmwValidators.passwordsMatch('password', 'confirmPassword') }
    );
  }

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
      control: this.registrationForm.get('email') ?? undefined,
      validator: () => !this.registrationForm.get('email')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'email-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'email',
      control: this.registrationForm.get('email') ?? undefined,
      validator: () => !this.registrationForm.get('email')?.hasError('email')
    });

    // Password validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'password-required',
      message: 'Password is required',
      severity: 'error',
      field: 'password',
      control: this.registrationForm.get('password') ?? undefined,
      validator: () => !this.registrationForm.get('password')?.hasError('required')
    });

    for (const req of PASSWORD_REQUIREMENTS) {
      this.validationService.addViolation(this.validationContext.id, {
        id: `password-${req.key}`,
        message: req.label,
        severity: 'error',
        field: 'password',
        control: this.registrationForm.get('password') ?? undefined,
        validator: () => !this.registrationForm.get('password')?.hasError(req.key)
      });
    }

    // Confirm password validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'confirmPassword-required',
      message: 'Confirm password is required',
      severity: 'error',
      field: 'confirmPassword',
      control: this.registrationForm.get('confirmPassword') ?? undefined,
      validator: () => !this.registrationForm.get('confirmPassword')?.hasError('required')
    });

    // Password match validation (form-level)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'passwords-mismatch',
      message: 'Passwords do not match',
      severity: 'error',
      field: 'confirmPassword',
      validator: () => !this.registrationForm.hasError('mismatch')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  /**
   * Handles the registration form submission.
   */
  onSubmit(): void {
    this.registrationForm.markAllAsTouched();
    this.registrationForm.updateValueAndValidity();

    if (this.registrationForm.invalid) {
      this.notificationService.warning(
        'Please fill all required fields and correct errors.'
      );
      return;
    }

    const userData: RegisterUser = this.registrationForm.getRawValue();

    // Register then auto-login with global loading overlay
    this.authService.register(userData).pipe(
      loading('Creating your account...'),
      switchMap(() => {
        this.authManagerService.rememberMe = true;
        const loginCredentials = {
          email: userData.email,
          password: userData.password,
          twoFactorCode: '',
          toFactorRecoveryCode: '',
          rememberMe: true,
        };
        return this.authManagerService.login(loginCredentials).pipe(
          loading('Signing you in...')
        );
      })
    ).subscribe({
      next: () => {
        this.notificationService.success(
          'Registration successful! You are now logged in.'
        );
        this.registrationForm.reset();
        this.registrationForm.setErrors(null);
        this.router.navigate(['/onboarding']);
      },
      error: (error) => {
        const errorMessage = error.message || ERROR_MESSAGES.AUTH.REGISTER_FAILED;
        this.notificationService.error(errorMessage);
        console.error('Registration error:', error);
      },
    });
  }

  openUserMenuFromFooter() {
    // Navigate to home page where the login popover is available
    this.router.navigate(['/home']);
  }
}
