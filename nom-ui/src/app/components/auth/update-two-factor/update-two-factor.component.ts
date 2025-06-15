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
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatListModule } from '@angular/material/list';

import { AuthService } from '../auth.service';
import { UpdateTwoFactorResponse } from '../models/update-two-factor-response';
import { UpdateTwoFactor } from '../models/update-two-factor'; // Import the UpdateTwoFactor model
import { NotificationService } from '../../../utilities/services/notification.service';

@Component({
  selector: 'app-update-two-factor',
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
    MatSlideToggleModule,
    MatListModule,
  ],
  templateUrl: './update-two-factor.component.html',
  styleUrls: ['./update-two-factor.component.scss'],
  encapsulation: ViewEncapsulation.None,
})
export class UpdateTwoFactorComponent implements OnInit {
  twoFactorForm!: FormGroup;
  isLoading = false; // For form submission

  // We cannot fetch initial status from backend without getTwoFactorStatus
  // So, initialize with a default "disabled" status
  current2faStatus: UpdateTwoFactorResponse = {
    sharedKey: '',
    recoveryCodesLeft: 0,
    recoverCodes: [],
    isTwoFactorEnabled: false, // Default to disabled
    isMachineRemembered: false,
  };

  sharedKeyToDisplay: string | null = null; // For display and QR code
  recoveryCodesToDisplay: string[] = []; // For displaying new recovery codes

  constructor(
    private nonNullableFb: NonNullableFormBuilder,
    private authService: AuthService,
    private notificationService: NotificationService
  ) {}

  ngOnInit(): void {
    this.twoFactorForm = this.nonNullableFb.group(
      {
        enable2fa: [this.current2faStatus.isTwoFactorEnabled], // Initialize with default disabled status
        password: ['', Validators.required],
        twoFactorCode: [''], // Required when disabling
        rememberMachine: [this.current2faStatus.isMachineRemembered], // This checkbox maps to forgetMachine
      },
      { validators: this.twoFactorConditionalValidator }
    );

    // Watch for changes in enable2fa toggle to adjust validation
    this.twoFactorForm.get('enable2fa')?.valueChanges.subscribe((value) => {
      this.twoFactorForm.get('password')?.updateValueAndValidity();
      this.twoFactorForm.get('twoFactorCode')?.updateValueAndValidity();
    });
  }

  // Form-level validator for 2FA logic
  twoFactorConditionalValidator(control: AbstractControl) {
    const enable2fa = control.get('enable2fa')?.value;
    const passwordControl = control.get('password');
    const twoFactorCodeControl = control.get('twoFactorCode');

    // Ensure password is required for any update operation
    if (!passwordControl?.value) {
      passwordControl?.setErrors({ required: true });
    } else {
      passwordControl?.setErrors(null);
    }

    // Two-factor code required when disabling 2FA
    // The code field is only shown if enable2fa is false in HTML,
    // so this validator will align with that.
    if (passwordControl?.valid && !enable2fa) {
      if (!twoFactorCodeControl?.value) {
        twoFactorCodeControl?.setErrors({ required: true });
      } else {
        twoFactorCodeControl?.setErrors(null);
      }
    } else {
      // If enabling or password not valid, clear code errors
      twoFactorCodeControl?.setErrors(null);
    }

    // If password field is valid and was marked required, clear its error
    if (passwordControl?.valid && passwordControl?.hasError('required')) {
      passwordControl.setErrors(null);
    }

    return null; // No form-level errors for now, specific control errors are set
  }

  /**
   * Generates the otpauth URI for QR code display.
   * @param sharedKey The base32 encoded shared key from the backend.
   * @param email The user's email address.
   * @param issuer The issuer name (your app's name).
   * @returns The otpauth URI string.
   */
  generateOtpAuthUri(
    sharedKey: string,
    email: string,
    issuer: string = 'NutritionOptimizationMachine'
  ): string {
    const encodedIssuer = encodeURIComponent(issuer);
    const encodedEmail = encodeURIComponent(email);
    return `otpauth://totp/${encodedIssuer}:${encodedEmail}?secret=${sharedKey}&issuer=${encodedIssuer}`;
  }

  /**
   * Handles the form submission for 2FA changes.
   */
  onSubmit(): void {
    this.twoFactorForm.markAllAsTouched(); // Mark all controls as touched
    this.twoFactorForm.updateValueAndValidity(); // Recalculate validation state

    if (this.twoFactorForm.invalid) {
      this.notificationService.warning(
        'Please correct the highlighted errors in the form.'
      );
      return;
    }

    this.isLoading = true;
    const formData = this.twoFactorForm.getRawValue();

    // Construct updateData according to the UpdateTwoFactor interface
    const updateData: UpdateTwoFactor = {
      enable: formData.enable2fa,
      twoFactorCode: formData.twoFactorCode, // twoFactorCode is now a string, not optional
      resetSharedKey: false, // Assuming these will be controlled by separate UI elements if needed
      resetRecoverCodes: false, // Assuming these will be controlled by separate UI elements if needed
      forgetMachine: !formData.rememberMachine, // Invert rememberMachine to map to forgetMachine
    };

    this.authService.updateTwoFactorAuth(updateData).subscribe({
      next: (response: UpdateTwoFactorResponse) => {
        this.isLoading = false;
        this.current2faStatus = response; // Update the component's status with the latest from the backend
        this.twoFactorForm.get('password')?.reset(); // Clear password after operation
        this.twoFactorForm.get('twoFactorCode')?.reset(); // Clear code

        // Determine success message and display setup info based on the response's isTwoFactorEnabled
        if (response.isTwoFactorEnabled) {
          this.sharedKeyToDisplay = response.sharedKey;
          this.recoveryCodesToDisplay = response.recoverCodes;
          this.notificationService.success(
            'Two-Factor Authentication enabled successfully! Please save your recovery codes.'
          );
        } else {
          // 2FA was disabled
          this.sharedKeyToDisplay = null;
          this.recoveryCodesToDisplay = [];
          this.notificationService.success(
            'Two-Factor Authentication disabled successfully!'
          );
        }

        // Ensure the toggle reflects the actual state returned from the backend
        this.twoFactorForm.patchValue(
          {
            enable2fa: response.isTwoFactorEnabled,
            rememberMachine: response.isMachineRemembered, // Assuming rememberMachine directly maps to isMachineRemembered
          },
          { emitEvent: false }
        ); // Don't trigger valueChanges observer again, preventing loop

        this.twoFactorForm.setErrors(null); // Reset form-level errors
      },
      error: (error) => {
        this.isLoading = false;
        console.error('2FA update error:', error);
        this.notificationService.error(
          error.message || 'Failed to update 2FA settings.'
        );
      },
    });
  }

  // Helper to copy recovery codes to clipboard
  copyRecoveryCodes(): void {
    const codesText = this.recoveryCodesToDisplay.join('\n');
    navigator.clipboard
      .writeText(codesText)
      .then(() => {
        this.notificationService.info('Recovery codes copied to clipboard!');
      })
      .catch((err) => {
        console.error('Could not copy text: ', err);
        this.notificationService.error(
          'Failed to copy codes. Please copy manually.'
        );
      });
  }

  // Helper to copy shared key to clipboard
  copySharedKey(): void {
    if (this.sharedKeyToDisplay) {
      navigator.clipboard
        .writeText(this.sharedKeyToDisplay)
        .then(() => {
          this.notificationService.info('Shared key copied to clipboard!');
        })
        .catch((err) => {
          console.error('Could not copy text: ', err);
          this.notificationService.error(
            'Failed to copy shared key. Please copy manually.'
          );
        });
    }
  }
}
