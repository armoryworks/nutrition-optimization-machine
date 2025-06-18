import { Component, OnInit, ViewEncapsulation } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';
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
import { CurrentInfo } from '../models/current-info';
import { NotificationService } from '../../utilities/services/notification.service';

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
  isLoading = false; // For form submission loading
  isInitialLoading = true; // For initial data load loading

  currentEmail: string | null = null;
  isEmailConfirmed: boolean | null = null;

  constructor(
    private nonNullableFb: NonNullableFormBuilder,
    private authService: AuthService,
    private notificationService: NotificationService // Use NotificationService
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

    // Load initial user info
    this.loadCurrentUserInfo();
  }

  private loadCurrentUserInfo(): void {
    this.isInitialLoading = true;
    this.authService.getInfo().subscribe({
      next: (info: CurrentInfo) => {
        this.currentEmail = info.email;
        this.isEmailConfirmed = info.isEmailConfirmed;

        this.updateInfoForm.patchValue({
          newEmail: info.email,
        });
        this.isInitialLoading = false;
      },
      error: (error) => {
        this.isInitialLoading = false;
        console.error('Error loading user info:', error);
        this.notificationService.error(
          error.message || 'Failed to load current user information.'
        );
      },
    });
  }

  // Custom validator to ensure newPassword has confirmNewPassword if newPassword is provided
  // and that oldPassword is required if either newEmail or newPassword is provided
  updateInfoConditionalValidator(control: AbstractControl) {
    const newPasswordControl = control.get('newPassword');
    const oldPasswordControl = control.get('oldPassword');
    const newEmailControl = control.get('newEmail');

    const newPassword = newPasswordControl?.value;
    const oldPassword = oldPasswordControl?.value;
    const newEmail = newEmailControl?.value;

    // Reset errors first to avoid stale errors from previous runs
    if (oldPasswordControl?.hasError('required') && !newPassword && !newEmail) {
      oldPasswordControl.setErrors(null);
    }

    // If new password or new email is provided, old password is required
    if ((newPassword || newEmail) && !oldPassword) {
      oldPasswordControl?.setErrors({ required: true });
    } else if (
      !newPassword &&
      !newEmail &&
      oldPasswordControl?.hasError('required')
    ) {
      oldPasswordControl?.setErrors(null);
    }

    // Ensure at least one field (newEmail or newPassword) is provided for update if oldPassword is present
    if (oldPassword && !newEmail && !newPassword) {
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
      this.notificationService.warning(
        'Please correct the highlighted errors in the form.'
      );
      return;
    }

    const formData = this.updateInfoForm.getRawValue();
    const updateData: UpdateInfo = {
      newEmail: formData.newEmail === '' ? undefined : formData.newEmail,
      newPassword:
        formData.newPassword === '' ? undefined : formData.newPassword,
      oldPassword: formData.oldPassword,
    };

    // This check ensures that an update operation is intended
    if (!updateData.newEmail && !updateData.newPassword) {
      this.notificationService.warning(
        'Please provide a new email or a new password to update.'
      );
      return;
    }

    this.isLoading = true;
    // updateInfo now returns Observable<void>, so 'response' won't be available in next callback
    this.authService.updateInfo(updateData).subscribe({
      next: () => {
        // No 'response' parameter here as it's Observable<void>
        this.isLoading = false;
        // Generic success message since specific message is not returned by service
        this.notificationService.success(
          'Account information updated successfully!'
        );

        // Reset form and clear errors
        this.updateInfoForm.reset({
          newEmail: updateData.newEmail, // Preserve the new email in the form
        });
        this.updateInfoForm.get('oldPassword')?.setErrors(null);
        this.updateInfoForm.setErrors(null);

        // Update current email displayed in component if it was changed
        if (updateData.newEmail) {
          this.currentEmail = updateData.newEmail;
        }
      },
      error: (error) => {
        this.isLoading = false;
        console.error('Update info error:', error);
        this.notificationService.error(
          error.message ||
            'An unexpected error occurred during update. Please try again.'
        );
      },
    });
  }
}
