import { Component, inject, signal, OnInit, OnDestroy } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwDividerComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../../auth.service';

@Component({
  selector: 'nom-login-popover',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwDividerComponent,
    AmwValidationTooltipDirective,
  ],
  templateUrl: './login-popover.component.html',
  styleUrls: ['./login-popover.component.scss']
})
export class LoginPopoverComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private authService = inject(AuthService);
  private router = inject(Router);
  private validationService = inject(AmwValidationService);

  loginForm: FormGroup;
  isLoading = signal(false);
  errorMessage = signal('');
  validationContext!: ValidationContext;

  constructor() {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]]
    });
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

    // Password validations
    this.validationService.addViolation(this.validationContext.id, {
      id: 'password-required',
      message: 'Password is required',
      severity: 'error',
      field: 'password',
      control: this.loginForm.get('password') ?? undefined,
      validator: () => !this.loginForm.get('password')?.hasError('required')
    });

    this.validationService.addViolation(this.validationContext.id, {
      id: 'password-minlength',
      message: 'Password must be at least 6 characters',
      severity: 'error',
      field: 'password',
      control: this.loginForm.get('password') ?? undefined,
      validator: () => !this.loginForm.get('password')?.hasError('minlength')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  onSubmit(): void {
    if (this.loginForm.valid) {
      this.isLoading.set(true);
      this.errorMessage.set('');

      const credentials = {
        email: this.loginForm.value.email,
        password: this.loginForm.value.password
      };

      this.authService.login(credentials).subscribe({
        next: () => {
          this.isLoading.set(false);
          // Close popover and navigate to dashboard
          this.router.navigate(['/user/dashboard']);
        },
        error: (error) => {
          this.isLoading.set(false);
          this.errorMessage.set(error.message || 'Login failed. Please try again.');
        }
      });
    }
  }

  onRegister(): void {
    this.router.navigate(['/register']);
  }
}

