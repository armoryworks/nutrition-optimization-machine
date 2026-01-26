import { Component, OnInit, OnDestroy, ViewEncapsulation, inject, signal } from '@angular/core';
import {
  FormGroup,
  Validators,
  ReactiveFormsModule,
  NonNullableFormBuilder,
  AbstractControl,
} from '@angular/forms';

import { AmwInputComponent, AmwButtonComponent, AmwCardComponent, AmwProgressBarComponent, AmwValidationTooltipDirective, AmwValidationService, ValidationContext } from 'angular-material-wrap';

import { AuthService } from '../auth.service';
import { UpdateInfo } from '../models/update-info';
import { CurrentInfo } from '../models/current-info';
import { NotificationService } from '../../utilities/services/notification.service';

@Component({
  selector: 'nom-update-info',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    AmwInputComponent,
    AmwButtonComponent,
    AmwCardComponent,
    AmwProgressBarComponent,
    AmwValidationTooltipDirective
  ],
  templateUrl: './update-info.component.html',
  styleUrls: ['./update-info.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class UpdateInfoComponent implements OnInit, OnDestroy {
  private nonNullableFb = inject(NonNullableFormBuilder);
  private authService = inject(AuthService);
  private notificationService = inject(NotificationService);
  private validationService = inject(AmwValidationService);

  updateInfoForm: FormGroup;
  isLoading = signal(false); // For form submission loading
  isInitialLoading = signal(true); // For initial data load loading

  currentEmail: string | null = null;
  isEmailConfirmed: boolean | null = null;
  validationContext!: ValidationContext;

  constructor() {
    this.updateInfoForm = this.nonNullableFb.group(
      {
        newEmail: ['', Validators.email],
        newPassword: ['', [Validators.minLength(8)]],
        oldPassword: ['', Validators.required],
      },
      { validators: this.updateInfoConditionalValidator }
    );
  }

  ngOnInit(): void {
    // Load initial user info
    this.loadCurrentUserInfo();

    this.validationContext = this.validationService.createContext({
      disableOnErrors: true
    });

    // Email validation (optional field)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'newEmail-format',
      message: 'Please enter a valid email address',
      severity: 'error',
      field: 'newEmail',
      control: this.updateInfoForm.get('newEmail') ?? undefined,
      validator: () => !this.updateInfoForm.get('newEmail')?.hasError('email')
    });

    // New password validation (optional field)
    this.validationService.addViolation(this.validationContext.id, {
      id: 'newPassword-minlength',
      message: 'Password must be at least 8 characters',
      severity: 'error',
      field: 'newPassword',
      control: this.updateInfoForm.get('newPassword') ?? undefined,
      validator: () => !this.updateInfoForm.get('newPassword')?.hasError('minlength')
    });

    // Old password validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'oldPassword-required',
      message: 'Current password is required',
      severity: 'error',
      field: 'oldPassword',
      control: this.updateInfoForm.get('oldPassword') ?? undefined,
      validator: () => !this.updateInfoForm.get('oldPassword')?.hasError('required')
    });

    // Form-level validation
    this.validationService.addViolation(this.validationContext.id, {
      id: 'noUpdateFields',
      message: 'Please provide a new email or a new password to update',
      severity: 'error',
      field: 'oldPassword',
      validator: () => !this.updateInfoForm.hasError('noUpdateFields')
    });
  }

  ngOnDestroy(): void {
    if (this.validationContext) {
      this.validationService.destroyContext(this.validationContext.id);
    }
  }

  private loadCurrentUserInfo(): void {
    this.isInitialLoading.set(true);
    this.authService.getInfo().subscribe({
      next: (info: CurrentInfo) => {
        this.currentEmail = info.email;
        this.isEmailConfirmed = info.isEmailConfirmed;

        this.updateInfoForm.patchValue({
          newEmail: info.email,
        });
        this.isInitialLoading.set(false);
      },
      error: (error) => {
        this.isInitialLoading.set(false);
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

    this.isLoading.set(true);
    // updateInfo now returns Observable<void>, so 'response' won't be available in next callback
    this.authService.updateInfo(updateData).subscribe({
      next: () => {
        // No 'response' parameter here as it's Observable<void>
        this.isLoading.set(false);
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
        this.isLoading.set(false);
        console.error('Update info error:', error);
        this.notificationService.error(
          error.message ||
          'An unexpected error occurred during update. Please try again.'
        );
      },
    });
  }
}
