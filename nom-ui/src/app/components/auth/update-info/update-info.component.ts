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

// Angular Material Imports
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { AuthService } from '../auth.service';
import { UpdateInfo } from '../models/update-info';

@Component({
  selector: 'app-update-info',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
  ],
  templateUrl: './update-info.component.html',
  styleUrls: ['./update-info.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class UpdateInfoComponent implements OnInit {
  updateInfoForm!: FormGroup;
  isLoading = false;

  constructor(
    private nonNullableFb: NonNullableFormBuilder, // Use NonNullableFormBuilder
    private authService: AuthService,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.updateInfoForm = this.nonNullableFb.group(
      {
        newEmail: ['', Validators.email],
        newPassword: ['', [Validators.minLength(8)]],
        oldPassword: ['', Validators.required],
      },
      { validators: this.updateInfoConditionalValidator }
    );
  }

  // Custom validator to ensure newPassword has confirmNewPassword if newPassword is provided
  // and that oldPassword is required if either newEmail or newPassword is provided
  updateInfoConditionalValidator(control: AbstractControl) {
    const newPassword = control.get('newPassword')?.value;
    const oldPassword = control.get('oldPassword')?.value;
    const newEmail = control.get('newEmail')?.value;

    // If a new password is provided, old password is required
    if (newPassword && !oldPassword) {
      control.get('oldPassword')?.setErrors({ required: true });
    } else {
      if (
        !newPassword &&
        !newEmail &&
        control.get('oldPassword')?.hasError('required')
      ) {
        control.get('oldPassword')?.setErrors(null);
      }
    }

    // If new email is provided, old password is required
    if (newEmail && !oldPassword) {
      control.get('oldPassword')?.setErrors({ required: true });
    } else {
      if (
        !newPassword &&
        !newEmail &&
        control.get('oldPassword')?.hasError('required')
      ) {
        control.get('oldPassword')?.setErrors(null);
      }
    }

    // Ensure at least one field (newEmail or newPassword) is provided for update
    if (!newEmail && !newPassword && oldPassword) {
      return { noUpdateFields: true };
    }

    return null;
  }

  /**
   * Handles the update info form submission.
   */
  onSubmit(): void {
    this.updateInfoForm.markAllAsTouched();
    if (this.updateInfoForm.invalid) {
      this.snackBar.open('Please correct the form errors.', 'Close', {
        duration: 3000,
      });
      return;
    }

    const formData = this.updateInfoForm.getRawValue();
    const updateData: UpdateInfo = {
      newEmail: formData.newEmail === '' ? undefined : formData.newEmail,
      newPassword:
        formData.newPassword === '' ? undefined : formData.newPassword,
      oldPassword: formData.oldPassword,
    };

    if (!updateData.newEmail && !updateData.newPassword) {
      this.snackBar.open(
        'Please provide a new email or a new password to update.',
        'Close',
        { duration: 3000 }
      );
      return;
    }

    this.isLoading = true;
    this.authService.updateInfo(updateData).subscribe({
      next: (response) => {
        this.isLoading = false;
        if (response.success) {
          this.snackBar.open(response.message, 'Dismiss', { duration: 7000 });
          this.updateInfoForm.reset();
          this.updateInfoForm.get('oldPassword')?.setErrors(null);
          this.updateInfoForm.setErrors(null);
        } else {
          this.snackBar.open(response.message, 'Close', { duration: 5000 });
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Update info error:', error);
        this.snackBar.open(
          'An unexpected error occurred. Please try again.',
          'Close',
          { duration: 5000 }
        );
      },
    });
  }
}
