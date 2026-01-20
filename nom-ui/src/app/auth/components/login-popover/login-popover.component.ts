import { Component, inject, signal } from '@angular/core';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwDividerComponent } from 'angular-material-wrap';

import { AuthService } from '../../auth.service';

@Component({
  selector: 'nom-login-popover',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwDividerComponent
  ],
  templateUrl: './login-popover.component.html',
  styleUrls: ['./login-popover.component.scss']
})
export class LoginPopoverComponent {
  private fb = inject(FormBuilder);
  private authService = inject(AuthService);
  private router = inject(Router);

  loginForm: FormGroup;
  isLoading = signal(false);
  errorMessage = signal('');

  constructor() {
    this.loginForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(6)]]
    });
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

