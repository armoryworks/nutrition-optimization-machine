import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';
import { CommonModule } from '@angular/common';
// Router is no longer directly used for navigation in this component
// RouterLink is kept in imports if needed by the HTML template, but not for direct TS logic.
import { Router } from '@angular/router'; // Kept Router import if it's used elsewhere in the component beyond navigation

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox'; // Kept as it's in your provided starter code
import { MatToolbarModule } from '@angular/material/toolbar'; // Kept as it's in your provided starter code
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';

import { AuthService } from '../auth.service';
import { RegisterUser } from '../models/register-user';
import { NotificationService } from '../../../utilities/services/notification.service';
import { AuthManagerService } from '../../../utilities/services/auth-manager.service'; // Import AuthManagerService

@Component({
  selector: 'app-registration',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatCheckboxModule,
    MatToolbarModule, // Kept as it's in your provided starter code
    MatProgressSpinnerModule,
    MatProgressBarModule,
    // RouterLink, // Removed if no direct routerLink is needed in this component's HTML
  ],
  templateUrl: './registration.component.html',
  styleUrls: ['./registration.component.scss'], // Assuming .scss
  encapsulation: ViewEncapsulation.None,
})
export class RegistrationComponent implements OnInit {
  registrationForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private notificationService: NotificationService,
    // Router is no longer injected if only navigation from onSubmit is removed.
    // If router is not used elsewhere, you can remove it. For now, assuming it might be.
    private router: Router, // Kept Router injection if it's used elsewhere
    private authManagerService: AuthManagerService // Inject AuthManagerService
  ) {}

  ngOnInit(): void {
    this.registrationForm = this.nonNullableFb.group(
      {
        email: ['', [Validators.required, Validators.email]],
        password: ['', [Validators.required, Validators.minLength(8)]],
        confirmPassword: ['', Validators.required],
        acceptTerms: [false, Validators.requiredTrue],
      },
      { validators: this.passwordMatchValidator }
    );
  }

  // Custom validator for password matching (applied to the FormGroup)
  passwordMatchValidator(control: AbstractControl) {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;

    return password && confirmPassword && password === confirmPassword
      ? null
      : { mismatch: true };
  }

  /**
   * Handles the registration form submission.
   */
  onSubmit(): void {
    this.registrationForm.markAllAsTouched(); // Mark all fields as touched for immediate validation feedback
    this.registrationForm.updateValueAndValidity(); // Ensure validation state is updated

    if (this.registrationForm.invalid) {
      this.notificationService.warning(
        'Please fill all required fields and correct errors.'
      );
      return;
    }

    this.isLoading = true;
    const userData: RegisterUser = this.registrationForm.getRawValue();

    this.authService.register(userData).subscribe({
      next: () => {
        // AuthService.register returns Observable<void>, so no 'response' parameter
        this.isLoading = false;
        this.notificationService.success(
          'Registration successful! Check the user menu to log in.'
        );
        -this.registrationForm.reset(); // Reset form fields
        this.registrationForm.setErrors(null); // Clear form-level errors
      },
      error: (error) => {
        this.isLoading = false;
        const errorMessage = error.message; // Access the processed error message from AuthService
        this.notificationService.error(errorMessage); // Use NotificationService for error
        console.error('Registration failed:', error);
      },
    });
  }

  openUserMenuFromFooter() {
    this.authManagerService.openUserMenuSignal.next();
  }
}
