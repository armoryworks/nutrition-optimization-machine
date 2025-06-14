import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router'; // Import RouterLink for button navigation

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../auth.service';
import { RegisterUser } from '../models/register-user';
import { catchError, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { NotificationService } from '../../../utilities/services/notification.service';

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
    MatToolbarModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    RouterLink, // Add RouterLink
  ],
  templateUrl: './registration.component.html',
  styleUrls: ['./registration.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class RegistrationComponent implements OnInit {
  registrationForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private notificationService: NotificationService,
    private router: Router
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

  // Custom validator for password matching
  passwordMatchValidator(control: AbstractControl) {
    const password = control.get('password')?.value;
    const confirmPassword = control.get('confirmPassword')?.value;
    return password === confirmPassword ? null : { mismatch: true };
  }

  /**
   * Handles the registration form submission.
   */
  onSubmit(): void {
    if (this.registrationForm.invalid) {
      this.notificationService.info(
        'Please fill all required fields and correct errors.',
        3000,
        'Dismiss'
      );
      return;
    }

    this.isLoading = true;
    const userData: RegisterUser = this.registrationForm.getRawValue();

    this.authService.register(userData).subscribe({
      next: (response) => {
        this.isLoading = false;
        this.router.navigate(['/home']);
        this.notificationService.success(
          'Registration Was Successful',
          3000,
          'Dismiss'
        );
      },
      error: (error) => {
        this.isLoading = false;
        const errorMessage = error.message; // Access the processed error message
        this.notificationService.error(errorMessage, 5000, 'Dismiss');
        console.error('Registration failed:', error);
      },
    });
  }
}
